using System.Runtime.InteropServices;

namespace LibVpxFrameDecoder.Interop;

/// <summary>
/// Native layout of vpx_codec_ctx_t. CLong follows the C long size (4 bytes on Windows, 8 bytes on LP64).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VpxCodecContext
{
	public byte* Name;
	public nint DecoderInterface;
	public int Error;
	public byte* ErrorDetail;
	public CLong InitFlags;
	public nint Config;
	public nint PrivateData;
}
