using Android.OS;
using Microsoft.Maui.ApplicationModel;

namespace AndroidAutoMediaApp;

public sealed class LocalAudioPermission : Permissions.BasePlatformPermission
{
	public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
		Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
			? new[] { (Android.Manifest.Permission.ReadMediaAudio, true) }
			: new[] { (Android.Manifest.Permission.ReadExternalStorage, true) };
}
