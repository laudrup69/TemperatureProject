using Android.App;
using Android.Content.PM;
using Android.OS;

namespace GoveeMAUI;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges =
        ConfigChanges.ScreenSize   | ConfigChanges.Orientation |
        ConfigChanges.UiMode       | ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Android 13+: sin este permiso la monitorización en segundo plano funciona
        // igual, pero no se ve la notificación que avisa de que está activa.
        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            RequestPermissions([Android.Manifest.Permission.PostNotifications], 0);
        }
    }
}
