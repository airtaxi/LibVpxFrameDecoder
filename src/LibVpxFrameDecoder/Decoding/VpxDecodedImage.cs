namespace LibVpxFrameDecoder.Decoding;

/// <summary>
/// A decoded I420(+alpha) image view. The planes point into memory owned by libvpx,
/// so the view is only valid until the next decode call.
/// </summary>
internal readonly unsafe struct VpxDecodedImage(nint lumaPlane, int lumaStride, nint chromaUPlane, int chromaUStride, nint chromaVPlane, int chromaVStride, nint alphaPlane, int alphaStride, int width, int height, int format, int colorSpace, int colorRange)
{
	internal nint LumaPlane { get; } = lumaPlane;

	internal int LumaStride { get; } = lumaStride;

	internal nint ChromaUPlane { get; } = chromaUPlane;

	internal int ChromaUStride { get; } = chromaUStride;

	internal nint ChromaVPlane { get; } = chromaVPlane;

	internal int ChromaVStride { get; } = chromaVStride;

	internal nint AlphaPlane { get; } = alphaPlane;

	internal int AlphaStride { get; } = alphaStride;

	internal int Width { get; } = width;

	internal int Height { get; } = height;

	internal int Format { get; } = format;

	internal int ColorSpace { get; } = colorSpace;

	internal int ColorRange { get; } = colorRange;

	internal bool HasAlpha => AlphaPlane != 0;
}
