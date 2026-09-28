using System.Runtime.InteropServices;
using LibVpxFrameDecoder.Interop;

namespace LibVpxFrameDecoder;

/// <summary>Information about the loaded native libvpx runtime.</summary>
public static class VpxRuntime
{
	/// <summary>Version string reported by vpx.dll, for example "1.16.0".</summary>
	public static string LibraryVersionString
	{
		get
		{
			unsafe
			{
				var pointer = VpxNative.GetVersionString();
				return pointer == null ? string.Empty : Marshal.PtrToStringUTF8((nint)pointer) ?? string.Empty;
			}
		}
	}
}
