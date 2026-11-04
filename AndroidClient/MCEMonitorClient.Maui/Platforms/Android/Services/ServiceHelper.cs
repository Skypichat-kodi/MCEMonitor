using Android.Content;

namespace MCEMonitorClient.Maui.Platforms.Android.Services
{
    public static class ServiceHelper
    {
        public static void Start(Context context)
        {
            var intent = new Intent(context, typeof(PollingForegroundService));
            intent.SetAction(PollingForegroundService.ActionStart);

            // StartForegroundService est API 26+.
            // L'analyseur CA1416 ne reconnaît pas toujours le guard
            // OperatingSystem.IsAndroidVersionAtLeast() sur certaines versions du SDK.
            // On utilise un pragma ciblé pour garantir un build propre.
#pragma warning disable CA1416
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
#pragma warning restore CA1416
        }

        public static void Stop(Context context)
        {
            var intent = new Intent(context, typeof(PollingForegroundService));
            intent.SetAction(PollingForegroundService.ActionStop);
            context.StartService(intent);
        }
    }
}