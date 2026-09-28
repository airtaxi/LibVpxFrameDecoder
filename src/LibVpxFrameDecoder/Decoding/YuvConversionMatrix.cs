using LibVpxFrameDecoder.Interop;

namespace LibVpxFrameDecoder.Decoding;

/// <summary>
/// Resolves the YuvConstants objects that libyuv exports for the supported color matrices. The mirrored
/// (Yvu) copies belong to calls whose chroma planes are swapped, which is how libyuv itself produces the
/// end swapped byte order.
/// </summary>
internal static class YuvConversionMatrix
{
	private const int MatrixCount = 4;

	private static nint[] s_matrixPointers;

	/// <summary>Returns the constants pointer for a conversion, using the mirrored copy when the chroma planes are passed swapped.</summary>
	internal static nint GetPointer(VpxColorConversion conversion, bool mirrored)
	{
		var matrixPointers = s_matrixPointers ??= LoadMatrixPointers();
		var matrix = conversion switch
		{
			VpxColorConversion.Bt601Full => 1,
			VpxColorConversion.Bt709Studio => 2,
			VpxColorConversion.Bt709Full => 3,
			_ => 0,
		};
		return matrixPointers[mirrored ? matrix + MatrixCount : matrix];
	}

	/// <summary>Loads the four BT.601/BT.709 and studio/full range matrices followed by their mirrored copies.</summary>
	private static nint[] LoadMatrixPointers()
	{
		var library = YuvNative.LoadLibrary();
		return
		[
			YuvNative.GetExport(library, "kYuvI601Constants"),
			YuvNative.GetExport(library, "kYuvJPEGConstants"),
			YuvNative.GetExport(library, "kYuvH709Constants"),
			YuvNative.GetExport(library, "kYuvF709Constants"),
			YuvNative.GetExport(library, "kYvuI601Constants"),
			YuvNative.GetExport(library, "kYvuJPEGConstants"),
			YuvNative.GetExport(library, "kYvuH709Constants"),
			YuvNative.GetExport(library, "kYvuF709Constants"),
		];
	}
}
