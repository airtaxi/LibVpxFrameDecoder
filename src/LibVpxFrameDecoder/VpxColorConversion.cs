namespace LibVpxFrameDecoder;

/// <summary>YUV to RGB conversion matrix and range combinations.</summary>
public enum VpxColorConversion
{
	/// <summary>Pick the matrix and range from the color space and range found in the bitstream.</summary>
	Auto,
	Bt601Studio,
	Bt709Studio,
	Bt601Full,
	Bt709Full,
}
