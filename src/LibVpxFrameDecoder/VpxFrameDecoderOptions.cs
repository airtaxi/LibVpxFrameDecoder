namespace LibVpxFrameDecoder;

/// <summary>Options for decoding and pixel conversion.</summary>
public sealed class VpxFrameDecoderOptions
{
	public static readonly VpxFrameDecoderOptions Default = new();

	/// <summary>Pixel order of the output buffer. RGBA by default.</summary>
	public VpxPixelFormat PixelFormat { get; init; } = VpxPixelFormat.Rgba;

	/// <summary>Multiplies RGB by alpha so frames blend correctly with premultiplied alpha blending.</summary>
	public bool PremultiplyAlpha { get; init; } = true;

	/// <summary>Number of decoder threads. Defaults to half of the available processors, capped at 8.</summary>
	public uint ThreadCount { get; init; } = (uint)Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

	/// <summary>YUV to RGB conversion mode. Auto by default, based on the color space and range in the bitstream.</summary>
	public VpxColorConversion ColorConversion { get; init; } = VpxColorConversion.Auto;
}
