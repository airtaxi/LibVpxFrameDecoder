using LibVpxFrameDecoder;
using UIKit;

namespace IosProbe;

/// <summary>
/// Entry point of the probe app. The workflow only links the app, and a manual run in the simulator is a quick
/// check that the static native libraries resolve at run time.
/// </summary>
public static class Program
{
	private static void Main(string[] args)
	{
		try { Console.WriteLine($"libvpx version: {VpxRuntime.LibraryVersionString}"); }
		catch (Exception exception) { Console.WriteLine($"native library check failed: {exception}"); }

		UIApplication.Main(args, null, typeof(AppDelegate));
	}
}
