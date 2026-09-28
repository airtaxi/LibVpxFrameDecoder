using System.Runtime.InteropServices;

namespace LibVpxFrameDecoder.Interop;

/// <summary>Native layout of vpx_codec_dec_cfg_t. Width/Height are hints and may be zero.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VpxDecoderConfig
{
	public uint Threads;
	public uint Width;
	public uint Height;
}
