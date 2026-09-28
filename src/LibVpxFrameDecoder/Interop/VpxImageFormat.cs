namespace LibVpxFrameDecoder.Interop;

/// <summary>vpx_img_fmt_t values handled by this library.</summary>
internal enum VpxImageFormat
{
	I420 = 0x102,
	I422 = 0x105,
	I444 = 0x106,
	I420HighBitDepth = 0x902,
	I422HighBitDepth = 0x905,
	I444HighBitDepth = 0x906,
}
