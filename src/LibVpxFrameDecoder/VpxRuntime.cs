using System.Runtime.InteropServices;
using System.Text;
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

	/// <summary>
	/// Reports the runtime identifier of the process and the candidate paths of the native libraries together with
	/// the loader result of each path. Useful when a platform has no prebuilt binaries for the current runtime.
	/// </summary>
	public static string DescribeNativeLibraries()
	{
		var description = new StringBuilder();

		description.AppendLine($"runtime identifier: {RuntimeInformation.RuntimeIdentifier}");
		description.AppendLine($"process architecture: {RuntimeInformation.ProcessArchitecture}");
		description.AppendLine($"{VpxNative.LibraryName}:");
		description.Append(NativeLibraryResolver.DescribeCandidates(VpxNative.LibraryName));
		description.AppendLine($"{YuvNative.LibraryName}:");
		description.Append(NativeLibraryResolver.DescribeCandidates(YuvNative.LibraryName));

		return description.ToString();
	}
}
