using System.Runtime.ExceptionServices;
using LibVpxFrameDecoder.Interop;

namespace LibVpxFrameDecoder.Decoding;

/// <summary>
/// Owns the main and alpha libvpx decoder contexts. Alpha frames are decoded with a second context,
/// mirroring what FFmpeg's libvpx wrapper does for WebM alpha. The main and alpha frames of a packet are
/// decoded in parallel: the main frame on the calling thread and the alpha frame on a dedicated worker
/// thread. The calling side must stay on a single thread; the worker is created on demand.
/// </summary>
internal sealed unsafe class VpxFrameDecoder : IDisposable
{
	private readonly VpxCodecKind _codecKind;
	private readonly uint _threadCount;
	private VpxDecoderHandle _mainDecoder;
	private VpxDecoderHandle _alphaDecoder;
	private bool _disposed;

	// Alpha decode hand-off. The worker thread owns _alphaDecoder.
	private readonly ManualResetEventSlim _alphaRequest = new(false);
	private readonly ManualResetEventSlim _alphaFinished = new(false);
	private Thread _alphaWorker;
	private volatile bool _alphaStop;
	private ArraySegment<byte> _alphaSource;
	private VpxImage* _alphaImage;
	private bool _alphaHasImage;
	private bool _alphaWorkPending;
	private Exception _alphaError;

	internal VpxFrameDecoder(VpxCodecKind codecKind, uint threadCount)
	{
		_codecKind = codecKind;
		_threadCount = threadCount;
	}

	/// <summary>
	/// Decodes one packet (plus its optional alpha packet). Returns false when the packet produced no frame,
	/// which happens for VP9 invisible frames. The alpha packet is decoded even when the main frame produces
	/// no output, so the alpha stream stays in lockstep with the color stream.
	/// </summary>
	internal bool TryDecode(ReadOnlySpan<byte> data, ArraySegment<byte> alphaData, out VpxDecodedImage image)
	{
		_mainDecoder ??= VpxDecoderHandle.Create(_codecKind, _threadCount);

		// Start the alpha decode first so it overlaps the main decode.
		StartAlphaWork(alphaData);

		VpxImage* mainImage;
		try
		{
			mainImage = DecodeSingleFrame(_mainDecoder, data);
		}
		catch
		{
			// Join the worker before unwinding so the next call sees an idle decoder.
			try { CompleteAlphaWork(); } catch { /* the main decode failure is the one that matters */ }
			throw;
		}

		var alphaImage = CompleteAlphaWork();

		if (mainImage == null)
		{
			image = default;
			return false;
		}

		image = BuildImage(mainImage, alphaImage);
		return true;
	}

	/// <summary>Stops the alpha worker and drops both decoder contexts. The next decode recreates them.</summary>
	internal void Reset()
	{
		StopAlphaWorker();

		_mainDecoder?.Dispose();
		_mainDecoder = null;
		// The alpha decoder is disposed by the worker thread when it exits.
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		Reset();
		_alphaRequest.Dispose();
		_alphaFinished.Dispose();
	}

	private void StartAlphaWork(ArraySegment<byte> alphaData)
	{
		if (alphaData.Count == 0) return;

		if (_alphaWorker == null)
		{
			_alphaWorker = new Thread(AlphaWorkerLoop) { IsBackground = true, Name = "VpxAlphaDecode" };
			_alphaWorker.Start();
		}

		_alphaSource = alphaData;
		_alphaWorkPending = true;
		_alphaFinished.Reset();
		_alphaRequest.Set();
	}

	/// <summary>Waits for the alpha frame of the current packet. Returns <see langword="null"/> when there is none.</summary>
	private VpxImage* CompleteAlphaWork()
	{
		if (!_alphaWorkPending) return null;

		_alphaWorkPending = false;
		_alphaFinished.Wait();

		var error = _alphaError;
		if (error != null)
		{
			_alphaError = null;
			ExceptionDispatchInfo.Capture(error).Throw();
		}

		return _alphaHasImage ? _alphaImage : null;
	}

	private void AlphaWorkerLoop()
	{
		try
		{
			while (true)
			{
				_alphaRequest.Wait();
				_alphaRequest.Reset();
				if (_alphaStop) return;

				var source = _alphaSource;
				try
				{
					_alphaDecoder ??= VpxDecoderHandle.Create(_codecKind, _threadCount);
					_alphaImage = DecodeSingleFrame(_alphaDecoder, new ReadOnlySpan<byte>(source.Array, source.Offset, source.Count));
					_alphaHasImage = _alphaImage != null;
					_alphaError = null;
				}
				catch (Exception exception)
				{
					_alphaImage = null;
					_alphaHasImage = false;
					_alphaError = exception;
				}
				finally
				{
					_alphaSource = default;
					_alphaFinished.Set();
				}
			}
		}
		finally
		{
			// Reset() and Dispose() join this thread before the next decoder is created.
			_alphaDecoder?.Dispose();
			_alphaDecoder = null;
		}
	}

	private void StopAlphaWorker()
	{
		var worker = _alphaWorker;
		if (worker == null) return;

		_alphaWorker = null;
		_alphaStop = true;
		_alphaRequest.Set();
		worker.Join();

		_alphaStop = false;
		_alphaWorkPending = false;
		_alphaSource = default;
		_alphaError = null;
		_alphaHasImage = false;
	}

	private static unsafe VpxImage* DecodeSingleFrame(VpxDecoderHandle decoder, ReadOnlySpan<byte> data)
	{
		fixed (byte* dataPointer = data)
		{
			var result = VpxNative.DecoderDecode(decoder.Context, dataPointer, (uint)data.Length, null, 0);
			if (result == VpxNative.CodecCorruptFrame) return null;
			if (result != VpxNative.CodecOk) throw new VpxException(VpxNative.DescribeError(decoder.Context, "vpx_codec_decode"));
		}

		VpxImage* decodedImage = null;
		void* iterator = null;
		VpxImage* image;
		while ((image = VpxNative.DecoderGetFrame(decoder.Context, &iterator)) != null) decodedImage = image;
		return decodedImage;
	}

	private static unsafe VpxDecodedImage BuildImage(VpxImage* mainImage, VpxImage* alphaImage)
	{
		// FFmpeg rejects alpha frames whose dimensions differ, so a mismatch is treated as opaque here.
		var hasAlpha = alphaImage != null && alphaImage->DisplayWidth == mainImage->DisplayWidth && alphaImage->DisplayHeight == mainImage->DisplayHeight;
		return new VpxDecodedImage(mainImage->LumaPlane, mainImage->LumaStride, mainImage->ChromaUPlane, mainImage->ChromaUStride, mainImage->ChromaVPlane, mainImage->ChromaVStride, hasAlpha ? alphaImage->LumaPlane : 0, hasAlpha ? alphaImage->LumaStride : 0, (int)mainImage->DisplayWidth, (int)mainImage->DisplayHeight, mainImage->Format, mainImage->ColorSpace, mainImage->ColorRange);
	}
}
