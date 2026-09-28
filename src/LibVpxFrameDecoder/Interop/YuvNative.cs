using System.Runtime.InteropServices;

namespace LibVpxFrameDecoder.Interop;

/// <summary>
/// Native libyuv declarations. The matrix conversions take a YuvConstants object that libyuv exports as a
/// data symbol (see <see cref="Decoding.YuvConversionMatrix"/>). libyuv.dll is produced by
/// scripts/build-native-libs.ps1.
/// </summary>
internal static partial class YuvNative
{
	internal const string LibraryName = "libyuv";

	static YuvNative() => NativeLibraryResolver.EnsureRegistered();

	/// <summary>Loads libyuv so its exported YuvConstants symbols can be resolved.</summary>
	internal static nint LoadLibrary()
	{
		try { return NativeLibraryResolver.LoadLibrary(LibraryName); }
		catch (DllNotFoundException exception) { throw new VpxException($"The native libyuv library ('{LibraryName}.dll') could not be loaded. Build it with scripts/build-native-libs.ps1 and keep it next to the assembly.", exception); }
	}

	/// <summary>Resolves an exported symbol and reports a clear error when the loaded build does not provide it.</summary>
	internal static nint GetExport(nint library, string name)
	{
		if (NativeLibrary.TryGetExport(library, name, out var address)) return address;
		throw new VpxException($"The native libyuv library does not export '{name}'. Rebuild it with scripts/build-native-libs.ps1.");
	}

	/// <summary>Converts I420 to ARGB (byte order B, G, R, A in memory) with an explicit color matrix.</summary>
	[LibraryImport(LibraryName, EntryPoint = "I420ToARGBMatrix")]
	internal static unsafe partial int ConvertI420ToArgb(byte* luma, int lumaStride, byte* chromaFirst, int chromaFirstStride, byte* chromaSecond, int chromaSecondStride, byte* destination, int destinationStride, nint constants, int width, int height);

	/// <summary>Converts I420 with a separate alpha plane to ARGB, optionally premultiplying the color channels.</summary>
	[LibraryImport(LibraryName, EntryPoint = "I420AlphaToARGBMatrix")]
	internal static unsafe partial int ConvertI420AlphaToArgb(byte* luma, int lumaStride, byte* chromaFirst, int chromaFirstStride, byte* chromaSecond, int chromaSecondStride, byte* alpha, int alphaStride, byte* destination, int destinationStride, nint constants, int width, int height, int attenuate);
}
