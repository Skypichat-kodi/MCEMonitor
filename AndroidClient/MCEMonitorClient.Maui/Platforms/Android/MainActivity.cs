using Android.App;
using Android.Content.PM;
using Android.OS;

namespace MCEMonitorClient.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Platforms.Android.Services.AndroidNotificationService.CreateChannels(this);

        // POST_NOTIFICATIONS est API 33+ ; OperatingSystem.IsAndroidVersionAtLeast
        // est reconnu par l'analyseur CA1416.
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            if (CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications)
                != global::Android.Content.PM.Permission.Granted)
            {
                RequestPermissions(
                    new[] { global::Android.Manifest.Permission.PostNotifications },
                    1001);
            }
        }
    }
}