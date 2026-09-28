using LibVpxFrameDecoder.Interop;

namespace LibVpxFrameDecoder.Decoding;

/// <summary>
/// Owns the main and alpha libvpx decoder contexts. Alpha frames are decoded with a second context,
/// mirroring what FFmpeg's libvpx wrapper does for WebM alpha. Not thread safe.
/// </summary>
internal sealed unsafe class VpxFrameDecoder(VpxCodecKind codecKind, uint threadCount) : IDisposable
{
	private readonly VpxCodecKind _codecKind = codecKind;
	private readonly uint _threadCount = threadCount;
	private VpxDecoderHandle _mainDecoder;
	private VpxDecoderHandle _alphaDecoder;
	private bool _disposed;

	/// <summary>
	/// Decodes one packet (plus its optional alpha packet). Returns false when the packet produced no frame,
	/// which happens for VP9 invisible frames.
	/// </summary>
	internal bool TryDecode(ReadOnlySpan<byte> data, ReadOnlySpan<byte> alphaData, out VpxDecodedImage image)
	{
		_mainDecoder ??= VpxDecoderHandle.Create(_codecKind, _threadCount);

		var mainImage = DecodeSingleFrame(_mainDecoder, data);
		if (mainImage == null)
		{
			image = default;
			return false;
		}

		VpxImage* alphaImage = null;
		if (alphaData.Length > 0)
		{
			_alphaDecoder ??= VpxDecoderHandle.Create(_codecKind, _threadCount);
			alphaImage = DecodeSingleFrame(_alphaDecoder, alphaData);
		}

		image = BuildImage(mainImage, alphaImage);
		return true;
	}

	/// <summary>Drops both decoder contexts. The next decode recreates them.</summary>
	internal void Reset()
	{
		_mainDecoder?.Dispose();
		_mainDecoder = null;
		_alphaDecoder?.Dispose();
		_alphaDecoder = null;
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		Reset();
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
