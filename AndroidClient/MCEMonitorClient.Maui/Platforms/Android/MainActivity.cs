using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace MCEMonitorClient.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
          LaunchMode = LaunchMode.SingleTop,
          ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                                 ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                                 ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
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

        // Si l'app a été ouverte par une notification, naviguer vers la page de détail.
        HandleNotificationIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);

        // L'app était déjà ouverte, on reçoit un nouvel intent.
        HandleNotificationIntent(intent);
    }

    private void HandleNotificationIntent(Intent? intent)
    {
        if (intent == null)
        {
            System.Diagnostics.Trace.WriteLine("[NOTIF] HandleNotificationIntent: intent null");
            return;
        }

        string? serverId = intent.GetStringExtra("server_id");
        string? serviceType = intent.GetStringExtra("service_type");

        System.Diagnostics.Trace.WriteLine(
            $"[NOTIF] HandleNotificationIntent: serverId={serverId ?? "(null)"}, serviceType={serviceType ?? "(null)"}");

        if (string.IsNullOrEmpty(serverId))
        {
            System.Diagnostics.Trace.WriteLine("[NOTIF] serverId vide, abandon.");
            return;
        }

        // Consommer les extras pour éviter la double navigation
        // (rotation d'écran, retour au premier plan, etc.)
        intent.RemoveExtra("server_id");
        intent.RemoveExtra("service_type");

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                // Attendre que Shell soit prêt (jusqu'à 2 secondes)
                int attempts = 0;
                while (Shell.Current == null && attempts < 20)
                {
                    await Task.Delay(100);
                    attempts++;
                }

                if (Shell.Current == null)
                {
                    System.Diagnostics.Trace.WriteLine("[NOTIF] Shell.Current null après 2s, abandon");
                    return;
                }

                string route = Pages.ServerDetailRouter.GetRouteForService(serviceType, serverId);
                System.Diagnostics.Trace.WriteLine($"[NOTIF] Route calculée: '{route}'");

                if (!string.IsNullOrEmpty(route))
                {
                    await Shell.Current.GoToAsync(route);
                    System.Diagnostics.Trace.WriteLine("[NOTIF] GoToAsync OK");
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine("[NOTIF] Route vide, pas de navigation.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[NOTIF] Navigation ERROR: {ex}");
            }
        });
    }
}