using Android.App;
using Android.OS;
using Android.Util;
using LibVpxFrameDecoder;

namespace AndroidProbe;

/// <summary>
/// Minimal activity that loads the native libraries through the managed API. The APK contents are checked by
/// the workflow; launching the app is a quick manual check that the shared libraries resolve at run time.
/// </summary>
[Activity(Label = "LibVpxFrameDecoder Probe", MainLauncher = true)]
public class MainActivity : Activity
{
	private const string Tag = "LibVpxFrameDecoderProbe";

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);

		try { Log.Info(Tag, $"libvpx version: {VpxRuntime.LibraryVersionString}"); }
		catch (Exception exception) { Log.Error(Tag, exception.ToString()); }
	}
}
