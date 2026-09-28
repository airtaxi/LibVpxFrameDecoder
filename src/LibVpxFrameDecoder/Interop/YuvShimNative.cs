using System.Runtime.InteropServices;

namespace LibVpxFrameDecoder.Interop;

/// <summary>
/// Native declarations of the lvpxshim C helper. libyuv exports the YuvConstants objects as data symbols,
/// which dlsym cannot find in a statically linked iOS or Mac Catalyst binary, so the shim returns their
/// addresses. The shim is built by scripts/build-native-apple-static.sh.
/// </summary>
internal static partial class YuvShimNative
{
	internal const string LibraryName = "lvpxshim";

	static YuvShimNative() => NativeLibraryResolver.EnsureRegistered();

	/// <summary>Returns the address of one of the eight YuvConstants objects (0..7, see <see cref="Decoding.YuvConversionMatrix"/>).</summary>
	[LibraryImport(LibraryName, EntryPoint = "lvpx_yuv_constants")]
	private static partial nint GetConstants(int index);

	/// <summary>Returns the address of a YuvConstants object and reports a clear error when the index is unknown.</summary>
	internal static nint GetConstantPointer(int index)
	{
		var pointer = GetConstants(index);
		if (pointer != 0) return pointer;

		throw new VpxException($"The native lvpxshim library returned no YuvConstants object for index {index}.");
	}
}
