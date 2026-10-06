using Android.App;
using Android.Content;
using Android.OS;

namespace MCEMonitorClient.Maui.Platforms.Android.Services
{
    [BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = true)]
    [IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionLockedBootCompleted })]
    public class BootReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent == null) return;

            if (intent.Action == Intent.ActionBootCompleted ||
                intent.Action == Intent.ActionLockedBootCompleted)
            {
                global::Android.Util.Log.Info("MCEMonitor", "Boot reçu : démarrage du service");

                try
                {
                    ServiceHelper.Start(context);
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Error("MCEMonitor",
                        "Erreur démarrage au boot : " + ex.Message);
                }
            }
        }
    }
}