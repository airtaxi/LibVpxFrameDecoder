namespace LibVpxFrameDecoder;

/// <summary>Metadata of an opened WebM file.</summary>
public sealed class WebmVideoInfo(string codecId, int width, int height, TimeSpan duration, double framesPerSecond, bool hasAlpha)
{
	/// <summary>Codec id from the container, for example "V_VP9".</summary>
	public string CodecId { get; } = codecId;

	public int Width { get; } = width;

	public int Height { get; } = height;

	/// <summary>Duration reported by the container. Zero when the file does not declare one.</summary>
	public TimeSpan Duration { get; } = duration;

	/// <summary>Frames per second derived from the default frame duration. Zero when it is not declared.</summary>
	public double FramesPerSecond { get; } = framesPerSecond;

	/// <summary>True when the container declares alpha mode for the video track.</summary>
	public bool HasAlpha { get; } = hasAlpha;
}
