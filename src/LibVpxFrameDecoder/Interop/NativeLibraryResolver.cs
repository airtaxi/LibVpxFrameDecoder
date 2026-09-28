using System.Reflection;
using System.Runtime.InteropServices;

namespace LibVpxFrameDecoder.Interop;

/// <summary>
/// Resolves vpx.dll, libvpx.so or libvpx.dylib (and the matching libyuv library) from the
/// runtimes/&lt;rid&gt;/native folder of the output directory. The folder follows the runtime identifier of the
/// running process, so one build works on every supported platform and architecture.
/// </summary>
internal static class NativeLibraryResolver
{
	private static readonly object s_lock = new();

	private static bool s_registered;

	/// <summary>Registers the resolver once for the assembly that declares the native imports.</summary>
	internal static void EnsureRegistered()
	{
		if (s_registered) return;

		lock (s_lock)
		{
			if (s_registered) return;

			NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
			s_registered = true;
		}
	}

	/// <summary>
	/// Loads a native library from the same candidate paths the import resolver uses. Needed for callers that
	/// load a library explicitly (for example to read exported data symbols) instead of importing functions.
	/// </summary>
	internal static nint LoadLibrary(string libraryName)
	{
		foreach (var candidate in GetCandidates(GetFileName(libraryName)))
		{
			if (NativeLibrary.TryLoad(candidate, out var handle))
			{
				return handle;
			}
		}

		return NativeLibrary.Load(libraryName);
	}

	private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
	{
		foreach (var candidate in GetCandidates(GetFileName(libraryName)))
		{
			// An absolute path that does not exist fails fast, so the next candidate is tried immediately.
			if (NativeLibrary.TryLoad(candidate, out var handle))
			{
				return handle;
			}
		}

		// iOS links native code into the app binary, so the symbols live in the main program.
		if (IsIOS()) return NativeLibrary.GetMainProgramHandle();

		// Fall back to the default resolution (the folder next to the assembly and the system paths).
		return 0;
	}

	private static string GetFileName(string libraryName)
	{
		if (libraryName.Contains('.')) return libraryName;

		if (IsWindows()) return libraryName + ".dll";
		if (IsApple()) return "lib" + libraryName + ".dylib";

		return "lib" + libraryName + ".so";
	}

	private static IEnumerable<string> GetCandidates(string fileName)
	{
		var baseDirectory = AppContext.BaseDirectory;

		yield return Path.Combine(baseDirectory, "runtimes", GetRuntimeIdentifier(), "native", fileName);
		yield return Path.Combine(baseDirectory, fileName);
	}

	private static string GetRuntimeIdentifier()
	{
		var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;

		if (!string.IsNullOrEmpty(runtimeIdentifier)) return runtimeIdentifier;

		var architecture = RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X64 => "x64",
			Architecture.X86 => "x86",
			Architecture.Arm64 => "arm64",
			Architecture.Arm => "arm",
			_ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
		};

		return GetPlatformName() + "-" + architecture;
	}

	private static string GetPlatformName()
	{
		if (IsWindows()) return "win";
		if (IsAndroid()) return "android";
		if (IsIOS()) return "ios";
		if (IsMacCatalyst()) return "maccatalyst";
		if (IsApple()) return "osx";
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";

		return "unknown";
	}

	private static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

	private static bool IsApple() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

	private static bool IsAndroid() => RuntimeInformation.IsOSPlatform(OSPlatform.Create("ANDROID"));

	private static bool IsIOS() => RuntimeInformation.IsOSPlatform(OSPlatform.Create("IOS"));

	private static bool IsMacCatalyst() => RuntimeInformation.IsOSPlatform(OSPlatform.Create("MACCATALYST"));
}
