using LibVpxFrameDecoder.Interop;

namespace LibVpxFrameDecoder.Decoding;

/// <summary>
/// Converts I420(+alpha) images to RGBA/BGRA with libyuv. RGBA output calls the matrix conversions with the
/// chroma planes swapped and the mirrored (Yvu) constants, which is how libyuv produces that byte order.
/// </summary>
internal static unsafe class YuvImageConverter
{
	/// <summary>Converts the image and returns the number of bytes written.</summary>
	internal static int Convert(VpxDecodedImage image, byte[] destination, VpxPixelFormat pixelFormat, bool premultiplyAlpha, VpxColorConversion colorConversion)
	{
		if (image.Format != (int)VpxImageFormat.I420) throw new VpxException($"Unsupported libvpx image format 0x{image.Format:X}. Only I420 is supported.");

		var rgba = pixelFormat == VpxPixelFormat.Rgba;
		var constants = YuvConversionMatrix.GetPointer(ResolveConversion(colorConversion, image), rgba);
		fixed (byte* destinationPointer = destination)
		{
			var result = ConvertImage(image, destinationPointer, rgba, premultiplyAlpha, constants);
			if (result != 0) throw new VpxException($"libyuv failed to convert the frame (error code {result}).");
		}

		return image.Width * image.Height * 4;
	}

	private static int ConvertImage(VpxDecodedImage image, byte* destination, bool rgba, bool premultiplyAlpha, nint constants)
	{
		var luma = (byte*)image.LumaPlane;
		var chromaFirst = (byte*)(rgba ? image.ChromaVPlane : image.ChromaUPlane);
		var chromaFirstStride = rgba ? image.ChromaVStride : image.ChromaUStride;
		var chromaSecond = (byte*)(rgba ? image.ChromaUPlane : image.ChromaVPlane);
		var chromaSecondStride = rgba ? image.ChromaUStride : image.ChromaVStride;
		var destinationStride = image.Width * 4;

		if (!image.HasAlpha) return YuvNative.ConvertI420ToArgb(luma, image.LumaStride, chromaFirst, chromaFirstStride, chromaSecond, chromaSecondStride, destination, destinationStride, constants, image.Width, image.Height);
		return YuvNative.ConvertI420AlphaToArgb(luma, image.LumaStride, chromaFirst, chromaFirstStride, chromaSecond, chromaSecondStride, (byte*)image.AlphaPlane, image.AlphaStride, destination, destinationStride, constants, image.Width, image.Height, premultiplyAlpha ? 1 : 0);
	}

	/// <summary>Resolves the matrix and range, reading the color space and range of the bitstream when the option is Auto.</summary>
	private static VpxColorConversion ResolveConversion(VpxColorConversion colorConversion, VpxDecodedImage image)
	{
		if (colorConversion != VpxColorConversion.Auto) return colorConversion;
		var isFullRange = image.ColorRange == 1;
		// VPX_CS_BT_601 = 1 and VPX_CS_BT_709 = 2; every other color space falls back to BT.601.
		var isBt709 = image.ColorSpace == 2;
		return (isBt709, isFullRange) switch
		{
			(true, false) => VpxColorConversion.Bt709Studio,
			(true, true) => VpxColorConversion.Bt709Full,
			(false, true) => VpxColorConversion.Bt601Full,
			_ => VpxColorConversion.Bt601Studio,
		};
	}
}
