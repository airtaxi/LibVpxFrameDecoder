namespace LibVpxFrameDecoder;

/// <summary>Metadata of one decoded frame. The pixels themselves are exposed through <see cref="WebmVideo.Pixels"/>.</summary>
public readonly struct VpxFrameInfo(TimeSpan timestamp, int width, int height, bool hasAlpha)
{
	/// <summary>Presentation timestamp reported by the container.</summary>
	public TimeSpan Timestamp { get; } = timestamp;

	public int Width { get; } = width;

	public int Height { get; } = height;

	/// <summary>True when the frame carried an alpha frame alongside the color frame.</summary>
	public bool HasAlpha { get; } = hasAlpha;

	/// <summary>Length in bytes of this frame inside the pixel buffer.</summary>
	public int PixelLength => Width * Height * 4;
}
