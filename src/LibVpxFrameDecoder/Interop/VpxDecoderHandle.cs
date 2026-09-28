using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LibVpxFrameDecoder.Interop;

/// <summary>
/// Owns a single vpx_codec_ctx_t. Disposing (or finalization) destroys the codec and frees the context memory.
/// </summary>
internal sealed unsafe class VpxDecoderHandle : SafeHandleZeroOrMinusOneIsInvalid
{
	private VpxDecoderHandle() : base(true) { }

	internal VpxCodecContext* Context => (VpxCodecContext*)handle;

	internal static VpxDecoderHandle Create(VpxCodecKind codecKind, uint threadCount)
	{
		var decoderInterface = codecKind == VpxCodecKind.Vp9 ? VpxNative.GetVp9DecoderInterface() : VpxNative.GetVp8DecoderInterface();
		if (decoderInterface == 0) throw new VpxException($"vpx_codec_{codecKind.ToString().ToLowerInvariant()}_dx() returned a null interface.");

		var handle = new VpxDecoderHandle();
		var context = (VpxCodecContext*)NativeMemory.AlignedAlloc((nuint)sizeof(VpxCodecContext), 16);
		if (context == null) throw new OutOfMemoryException("Failed to allocate a vpx_codec_ctx_t.");
		NativeMemory.Clear(context, (nuint)sizeof(VpxCodecContext));
		handle.SetHandle((nint)context);

		var config = new VpxDecoderConfig { Threads = threadCount };
		var result = VpxNative.DecoderInitialize(context, decoderInterface, &config, new CLong(0), VpxNative.DecoderAbiVersion);
		if (result != VpxNative.CodecOk)
		{
			var message = VpxNative.DescribeError(context, "vpx_codec_dec_init_ver");
			handle.Dispose();
			throw new VpxException(message);
		}

		return handle;
	}

	protected override bool ReleaseHandle()
	{
		// vpx_codec_destroy is tolerant of contexts whose initialization failed and returns an error instead.
		VpxNative.DecoderDestroy((VpxCodecContext*)handle);
		NativeMemory.AlignedFree((void*)handle);
		return true;
	}
}
