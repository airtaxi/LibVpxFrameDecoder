using System.Runtime.InteropServices;

namespace LibVpxFrameDecoder.Interop;

/// <summary>
/// Native layout of vpx_image_t. The Luma/ChromaU/ChromaV/Alpha plane slots mirror planes[0..3].
/// The image points into memory owned by libvpx, so it is only valid until the next decode call.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VpxImage
{
	public int Format;
	public int ColorSpace;
	public int ColorRange;
	public uint Width;
	public uint Height;
	public uint BitDepth;
	public uint DisplayWidth;
	public uint DisplayHeight;
	public uint RenderWidth;
	public uint RenderHeight;
	public uint ChromaShiftX;
	public uint ChromaShiftY;
	public nint LumaPlane;
	public nint ChromaUPlane;
	public nint ChromaVPlane;
	public nint AlphaPlane;
	public int LumaStride;
	public int ChromaUStride;
	public int ChromaVStride;
	public int AlphaStride;
	public int BitsPerSample;
	public nint UserPrivate;
	public nint ImageData;
	public int ImageDataOwner;
	public int SelfAllocated;
	public nint FrameBufferPrivate;
}
