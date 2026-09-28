using System.Diagnostics;
using LibVpxFrameDecoder.Decoding;
using LibVpxFrameDecoder.Interop;
using LibVpxFrameDecoder.Webm;

namespace LibVpxFrameDecoder;

/// <summary>
/// Decodes a WebM (VP8/VP9) file frame by frame, including the alpha channel stored in WebM alpha blocks.
/// Instances are not thread safe: use one instance per player and keep it on a single decode thread.
/// </summary>
public sealed class WebmVideo : IDisposable
{
	private readonly WebmDemuxer _demuxer;
	private readonly VpxFrameDecoder _decoder;
	private readonly VpxFrameDecoderOptions _options;
	private byte[] _pixelBuffer = [];
	private int _pixelLength;
	private TimeSpan _skipUntil = TimeSpan.MinValue;
	private bool _disposed;

	private WebmVideo(WebmDemuxer demuxer, VpxFrameDecoderOptions options)
	{
		if (!demuxer.VideoTrack.IsVp8 && !demuxer.VideoTrack.IsVp9) throw new WebmFormatException($"Unsupported video codec '{demuxer.VideoTrack.CodecId}'. Only V_VP8 and V_VP9 are supported.");

		_demuxer = demuxer;
		_options = options;
		_decoder = new VpxFrameDecoder(demuxer.VideoTrack.IsVp9 ? VpxCodecKind.Vp9 : VpxCodecKind.Vp8, options.ThreadCount);
		Info = new WebmVideoInfo(demuxer.VideoTrack.CodecId, demuxer.VideoTrack.PixelWidth, demuxer.VideoTrack.PixelHeight, demuxer.Duration, GetFramesPerSecond(demuxer.VideoTrack), demuxer.VideoTrack.AlphaMode);
	}

	/// <summary>Metadata of the opened file.</summary>
	public WebmVideoInfo Info { get; }

	/// <summary>Reusable pixel buffer that holds the last decoded frame.</summary>
	public byte[] PixelBuffer => _pixelBuffer;

	/// <summary>Pixels of the last decoded frame. Valid until the next read or dispose.</summary>
	public ReadOnlySpan<byte> Pixels => _pixelBuffer.AsSpan(0, _pixelLength);

	/// <summary>Time spent inside libvpx while producing the last frame, including skipped frames. Zero when the stream ended.</summary>
	public TimeSpan LastDecodeTime { get; private set; }

	/// <summary>Time spent converting the last frame to RGBA/BGRA. Zero when the stream ended.</summary>
	public TimeSpan LastConvertTime { get; private set; }

	public static WebmVideo Open(string filePath) => Open(filePath, VpxFrameDecoderOptions.Default);

	public static WebmVideo Open(string filePath, VpxFrameDecoderOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var demuxer = WebmDemuxer.Open(filePath);
		try { return new WebmVideo(demuxer, options); }
		catch
		{
			demuxer.Dispose();
			throw;
		}
	}

	/// <summary>Decodes the next frame. Returns false at the end of the stream.</summary>
	public bool TryReadFrame(out VpxFrameInfo frame)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		var decodeStartTimestamp = Stopwatch.GetTimestamp();
		while (_demuxer.TryReadPacket(out var packet))
		{
			// Invisible frames produce no output but must still be decoded so later frames stay correct.
			if (!_decoder.TryDecode(_demuxer.PacketData, _demuxer.AlphaPacketData, out var image)) continue;
			if (packet.Timestamp < _skipUntil) continue;

			_skipUntil = TimeSpan.MinValue;
			LastDecodeTime = Stopwatch.GetElapsedTime(decodeStartTimestamp);

			var convertStartTimestamp = Stopwatch.GetTimestamp();
			_pixelLength = YuvImageConverter.Convert(image, EnsurePixelBuffer(image.Width * image.Height * 4), _options.PixelFormat, _options.PremultiplyAlpha, _options.ColorConversion);
			LastConvertTime = Stopwatch.GetElapsedTime(convertStartTimestamp);

			frame = new VpxFrameInfo(packet.Timestamp, image.Width, image.Height, image.HasAlpha);
			return true;
		}

		LastDecodeTime = Stopwatch.GetElapsedTime(decodeStartTimestamp);
		LastConvertTime = TimeSpan.Zero;
		frame = default;
		return false;
	}

	/// <summary>Moves to the cluster that contains the given time and skips frames until that time.</summary>
	public void Seek(TimeSpan position)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		_demuxer.Seek(position);
		_decoder.Reset();
		_skipUntil = position;
	}

	/// <summary>Rewinds to the start of the stream.</summary>
	public void Rewind() => Seek(TimeSpan.Zero);

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_decoder.Dispose();
		_demuxer.Dispose();
	}

	private byte[] EnsurePixelBuffer(int requiredLength)
	{
		if (_pixelBuffer.Length >= requiredLength) return _pixelBuffer;
		_pixelBuffer = new byte[requiredLength];
		return _pixelBuffer;
	}

	private static double GetFramesPerSecond(WebmTrackInfo track) => track.DefaultDurationNanoseconds > 0 ? 1_000_000_000d / track.DefaultDurationNanoseconds : 0d;
}
