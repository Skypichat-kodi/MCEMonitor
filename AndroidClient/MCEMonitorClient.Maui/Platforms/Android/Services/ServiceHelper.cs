using Android.Content;
using Android.OS;

namespace MCEMonitorClient.Maui.Platforms.Android.Services
{
    public static class ServiceHelper
    {
        public static void Start(Context context)
        {
            var intent = new Intent(context, typeof(PollingForegroundService));
            intent.SetAction(PollingForegroundService.ActionStart);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }

        public static void Stop(Context context)
        {
            var intent = new Intent(context, typeof(PollingForegroundService));
            intent.SetAction(PollingForegroundService.ActionStop);
            context.StartService(intent);
        }
    }
}