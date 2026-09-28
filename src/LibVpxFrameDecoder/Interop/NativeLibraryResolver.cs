using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace LibVpxFrameDecoder.Interop;

/// <summary>
/// Resolves vpx.dll, libvpx.so or libvpx.dylib (and the matching libyuv library) from the
/// runtimes/&lt;rid&gt;/native folder of the output directory. The folder follows the platform and architecture of
/// the running process, so one build works on every supported platform and architecture.
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
		var fileName = GetFileName(libraryName);

		foreach (var candidate in GetCandidates(fileName))
		{
			if (NativeLibrary.TryLoad(candidate, out var handle))
			{
				return handle;
			}
		}

		return NativeLibrary.Load(libraryName);
	}

	/// <summary>
	/// Describes the candidate paths of a native library and whether each one loads. Used by
	/// <see cref="VpxRuntime.DescribeNativeLibraries"/> to diagnose missing platform binaries.
	/// </summary>
	internal static string DescribeCandidates(string libraryName)
	{
		var description = new StringBuilder();

		foreach (var candidate in GetCandidates(GetFileName(libraryName)))
		{
			description.Append("  ").Append(candidate).Append(" -> ");

			try
			{
				NativeLibrary.Load(candidate);
				description.AppendLine("loaded");
			}
			catch (Exception exception) { description.AppendLine(exception.Message); }
		}

		return description.ToString();
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
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var candidate in EnumerateCandidates(fileName))
		{
			if (seen.Add(candidate))
			{
			    yield return candidate;
			}
		}
	}

	private static IEnumerable<string> EnumerateCandidates(string fileName)
	{
		var baseDirectory = AppContext.BaseDirectory;
		var runtimesDirectory = Path.Combine(baseDirectory, "runtimes");

		yield return Path.Combine(runtimesDirectory, GetRuntimeIdentifier(), "native", fileName);

		foreach (var candidate in GetRuntimesDirectoryCandidates(runtimesDirectory, fileName)) yield return candidate;

		yield return Path.Combine(baseDirectory, fileName);
	}

	/// <summary>
	/// Accepts any runtimes/&lt;platform&gt;-&lt;architecture&gt; folder as well, because the runtime identifier of the
	/// process can carry a distribution name (for example ubuntu.24.04-x64) that has no folder of its own.
	/// </summary>
	private static IEnumerable<string> GetRuntimesDirectoryCandidates(string runtimesDirectory, string fileName)
	{
		if (!Directory.Exists(runtimesDirectory)) yield break;

		var platformPrefix = GetPlatformName();
		var architectureSuffix = "-" + GetArchitectureName();

		foreach (var directory in Directory.EnumerateDirectories(runtimesDirectory))
		{
			var directoryName = Path.GetFileName(directory);

			if (!directoryName.StartsWith(platformPrefix, StringComparison.OrdinalIgnoreCase)) continue;
			if (!directoryName.EndsWith(architectureSuffix, StringComparison.OrdinalIgnoreCase)) continue;

			yield return Path.Combine(directory, "native", fileName);
		}
	}

	private static string GetRuntimeIdentifier()
	{
		var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;

		if (!string.IsNullOrEmpty(runtimeIdentifier)) return runtimeIdentifier;

		return GetPlatformName() + "-" + GetArchitectureName();
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

	private static string GetArchitectureName() => RuntimeInformation.ProcessArchitecture switch
	{
		Architecture.X64 => "x64",
		Architecture.X86 => "x86",
		Architecture.Arm64 => "arm64",
		Architecture.Arm => "arm",
		_ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
	};

	private static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

	private static bool IsApple() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

	private static bool IsAndroid() => RuntimeInformation.IsOSPlatform(OSPlatform.Create("ANDROID"));

	private static bool IsIOS() => RuntimeInformation.IsOSPlatform(OSPlatform.Create("IOS"));

	private static bool IsMacCatalyst() => RuntimeInformation.IsOSPlatform(OSPlatform.Create("MACCATALYST"));
}
