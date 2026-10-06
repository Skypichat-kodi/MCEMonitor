using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using AndroidX.Core.App;
using MCEMonitorClient.Shared.Models;
using System.Collections.Generic;

namespace MCEMonitorClient.Maui.Platforms.Android.Services
{
    public static class AndroidNotificationService
    {
        public const string ChannelCritical = "mcem_critical";
        public const string ChannelWarning  = "mcem_warning";
        public const string ChannelInfo     = "mcem_info";

        private const string ServiceChannel = "mcem_service";
        private static int _nextId = 1000;

        /// <summary>
        /// Crée les canaux de notification (une seule fois, idempotent).
        /// À appeler au démarrage (dans MainActivity.OnCreate ou MainApplication.OnCreate).
        /// </summary>
        public static void CreateChannels(Context context)
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                return;

            var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;

            // Canal CRITIQUE avec alarm.wav
            var criticalSound = global::Android.Net.Uri.Parse(
                $"android.resource://{context.PackageName}/{Resource.Raw.alarm}");
            var critical = new NotificationChannel(ChannelCritical, "Alertes critiques",
                NotificationImportance.High)
            {
                Description = "Alertes serveur critiques"
            };
            critical.SetSound(criticalSound, new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Notification).Build());
            critical.EnableVibration(true);

            // Canal WARNING avec warning.wav
            var warningSound = global::Android.Net.Uri.Parse(
                $"android.resource://{context.PackageName}/{Resource.Raw.warning}");
            var warning = new NotificationChannel(ChannelWarning, "Avertissements",
                NotificationImportance.Default)
            {
                Description = "Avertissements serveur"
            };
            warning.SetSound(warningSound, new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Notification).Build());

            // Canal INFO silencieux
            var info = new NotificationChannel(ChannelInfo, "Informations",
                NotificationImportance.Low)
            {
                Description = "Notifications informatives"
            };
            info.SetSound(null, null);

            // Canal du service (persistant)
            var service = new NotificationChannel(ServiceChannel, "Service de surveillance",
                NotificationImportance.Low)
            {
                Description = "Notification permanente du service"
            };
            service.SetSound(null, null);

            manager.CreateNotificationChannel(critical);
            manager.CreateNotificationChannel(warning);
            manager.CreateNotificationChannel(info);
            manager.CreateNotificationChannel(service);
        }

        /// <summary>
        /// Affiche une notification d'alerte serveur.
        /// </summary>
        public static void ShowAlert(Context context, PushAlert alert)
        {
            string channel = alert.Status switch
            {
                "critical" => ChannelCritical,
                "warning"  => ChannelWarning,
                _          => ChannelInfo
            };

            int priority = alert.Status switch
            {
                "critical" => (int)NotificationPriority.High,
                "warning"  => (int)NotificationPriority.Default,
                _          => (int)NotificationPriority.Low
            };

            string title = alert.Status switch
            {
                "critical" => $"{alert.ServerName} - CRITIQUE",
                "warning"  => $"{alert.ServerName} - Alerte",
                "offline"  => $"{alert.ServerName} - Hors ligne",
                "ok"       => $"{alert.ServerName} - OK",
                _          => alert.ServerName
            };

            string message = BuildAlertMessage(alert);

            var intent = new Intent(Intent.ActionView,
                global::Android.Net.Uri.Parse(alert.BaseUrl));
            var pending = PendingIntent.GetActivity(
                context, 0, intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            var notification = new NotificationCompat.Builder(context, channel)
                .SetContentTitle(title)
                .SetContentText(message)
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(message))
                .SetSmallIcon(Resource.Drawable.ic_stat_notify)
                .SetPriority(priority)
                .SetContentIntent(pending)
                .SetAutoCancel(true)
                .Build();

            var manager = NotificationManagerCompat.From(context);
            manager.Notify(_nextId++, notification);
        }

        /// <summary>
        /// Affiche une notification média (lecture démarrée / terminée).
        /// </summary>
        public static void ShowMedia(Context context, PushMedia media)
        {
            string title = media.EventType == "started"
                ? $"{media.ServerName} - Lecture en cours"
                : $"{media.ServerName} - Lecture terminée";

            string info = "";
            if (!string.IsNullOrEmpty(media.Client))
                info += $"Depuis : {media.Client}\n";
            info += string.IsNullOrEmpty(media.Title) ? media.MediaType : media.Title;
            if (media.Saison > 0 || media.Episode > 0)
                info += $"  ({media.Saison:00}x{media.Episode:00})";

            var intent = new Intent(Intent.ActionView,
                global::Android.Net.Uri.Parse(media.BaseUrl));
            var pending = PendingIntent.GetActivity(
                context, 0, intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            var notification = new NotificationCompat.Builder(context, ChannelInfo)
                .SetContentTitle(title)
                .SetContentText(info)
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(info))
                .SetSmallIcon(Resource.Drawable.ic_stat_notify)
                .SetPriority((int)NotificationPriority.Low)
                .SetContentIntent(pending)
                .SetAutoCancel(true)
                .Build();

            var manager = NotificationManagerCompat.From(context);
            manager.Notify(_nextId++, notification);
        }

        private static string BuildAlertMessage(PushAlert alert)
        {
            if (alert.Status == "ok" && alert.PreviousStatus != "ok")
                return "Le serveur est de nouveau joignable";

            if (alert.Status == "offline")
                return "Le serveur ne répond plus";

            if (alert.Problems != null && alert.Problems.Count > 0)
            {
                var lines = new List<string>();
                int maxShown = 3;

                for (int i = 0; i < System.Math.Min(alert.Problems.Count, maxShown); i++)
                {
                    var p = alert.Problems[i];
                    string prefix = p.Severity == "critical" ? "[CRIT]" : "[WARN]";
                    lines.Add($"{prefix} {p.Message}");
                }

                if (alert.Problems.Count > maxShown)
                    lines.Add($"... et {alert.Problems.Count - maxShown} autre(s)");

                return string.Join("\n", lines);
            }

            return alert.FirstProblem ?? "";
        }
    }
}