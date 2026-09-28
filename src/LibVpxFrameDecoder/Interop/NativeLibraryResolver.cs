using System.Reflection;
using System.Runtime.InteropServices;

namespace LibVpxFrameDecoder.Interop;

/// <summary>
/// Resolves vpx.dll and libyuv.dll from the runtimes/win-&lt;arch&gt;/native folder of the output directory.
/// The folder follows the architecture of the running process, so an AnyCPU build works both natively and
/// under x64 emulation on Windows on ARM.
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

		// Fall back to the default resolution (the folder next to the assembly and the system paths).
		return 0;
	}

	private static string GetFileName(string libraryName) => libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? libraryName : libraryName + ".dll";

	private static IEnumerable<string> GetCandidates(string fileName)
	{
		var baseDirectory = AppContext.BaseDirectory;

		yield return Path.Combine(baseDirectory, "runtimes", GetRuntimeIdentifier(), "native", fileName);
		yield return Path.Combine(baseDirectory, fileName);
	}

	private static string GetRuntimeIdentifier() => RuntimeInformation.ProcessArchitecture switch
	{
		Architecture.X64 => "win-x64",
		Architecture.Arm64 => "win-arm64",
		Architecture.X86 => "win-x86",
		Architecture.Arm => "win-arm",
		_ => "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
	};
}
