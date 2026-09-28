using System.Runtime.InteropServices;

namespace LibVpxFrameDecoder.Interop;

/// <summary>
/// Native libvpx declarations. Targets vpx.dll (libvpx 1.16.0) as built by vcpkg;
/// the ABI constants must match the vpx headers of that exact version.
/// </summary>
internal static partial class VpxNative
{
	internal const string LibraryName = "vpx";

	static VpxNative() => NativeLibraryResolver.EnsureRegistered();

	/// <summary>Decoder ABI version passed to vpx_codec_dec_init_ver (3 + VPX_CODEC_ABI_VERSION; 12 for 1.16.0).</summary>
	internal const int DecoderAbiVersion = 12;

	internal const int CodecOk = 0;

	internal const int CodecCorruptFrame = 7;

	[LibraryImport(LibraryName, EntryPoint = "vpx_codec_vp8_dx")]
	internal static partial nint GetVp8DecoderInterface();

	[LibraryImport(LibraryName, EntryPoint = "vpx_codec_vp9_dx")]
	internal static partial nint GetVp9DecoderInterface();

	[LibraryImport(LibraryName, EntryPoint = "vpx_codec_version")]
	internal static partial int GetVersion();

	[LibraryImport(LibraryName, EntryPoint = "vpx_codec_version_str")]
	internal static unsafe partial byte* GetVersionString();

	[LibraryImport(LibraryName, EntryPoint = "vpx_codec_dec_init_ver")]
	internal static unsafe partial int DecoderInitialize(VpxCodecContext* context, nint decoderInterface, VpxDecoderConfig* config, CLong flags, int version);

	[LibraryImport(LibraryName, EntryPoint = "vpx_codec_decode")]
	internal static unsafe partial int DecoderDecode(VpxCodecContext* context, byte* data, uint dataSize, void* userPrivate, int deadline);

	[LibraryImport(LibraryName, EntryPoint = "vpx_codec_get_frame")]
	internal static unsafe partial VpxImage* DecoderGetFrame(VpxCodecContext* context, void** iterator);

	[LibraryImport(LibraryName, EntryPoint = "vpx_codec_destroy")]
	internal static unsafe partial int DecoderDestroy(VpxCodecContext* context);

	[LibraryImport(LibraryName, EntryPoint = "vpx_codec_error")]
	internal static unsafe partial byte* GetError(VpxCodecContext* context);

	[LibraryImport(LibraryName, EntryPoint = "vpx_codec_error_detail")]
	internal static unsafe partial byte* GetErrorDetail(VpxCodecContext* context);

	/// <summary>Builds a readable message from the last error stored in a decoder context.</summary>
	internal static unsafe string DescribeError(VpxCodecContext* context, string operation)
	{
		var error = ReadNativeString(GetError(context));
		var detail = ReadNativeString(GetErrorDetail(context));
		if (string.IsNullOrEmpty(error)) error = "unknown error";
		return string.IsNullOrEmpty(detail) ? $"{operation} failed: {error}" : $"{operation} failed: {error} ({detail})";
	}

	private static unsafe string ReadNativeString(byte* pointer) => pointer == null ? string.Empty : Marshal.PtrToStringUTF8((nint)pointer) ?? string.Empty;
}
