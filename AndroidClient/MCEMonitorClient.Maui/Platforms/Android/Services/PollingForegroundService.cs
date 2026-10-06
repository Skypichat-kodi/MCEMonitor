using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using MCEMonitorClient.Shared.Models;
using MCEMonitorClient.Shared.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MCEMonitorClient.Maui.Platforms.Android.Services
{
    [Service(ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
    public class PollingForegroundService : Service
    {
        public const string ActionStart = "MCEM_START";
        public const string ActionStop  = "MCEM_STOP";

        private const string ServiceChannel = "mcem_service";
        private const int ServiceNotifId = 1;
        private const string Tag = "MCEMonitor";

        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private PollingEngine? _engine;
        private ServerConfigStore? _store;

        public override void OnCreate()
        {
            base.OnCreate();

            // Redirige les Debug.WriteLine vers logcat
            System.Diagnostics.Trace.Listeners.Clear();
            System.Diagnostics.Trace.Listeners.Add(new AndroidLogListener());

            global::Android.Util.Log.Info(Tag, "OnCreate du service");

            string configPath = System.IO.Path.Combine(
                FilesDir!.AbsolutePath, "MCEMonitorClient.config");

            global::Android.Util.Log.Info(Tag, "Chemin config : " + configPath);

            _store = new ServerConfigStore(configPath);
            _engine = new PollingEngine();

            _engine.OnStateChanged += OnStateChanged;
            _engine.OnMediaEvent += OnMediaEvent;
        }

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            global::Android.Util.Log.Info(Tag, "OnStartCommand : action = " + (intent?.Action ?? "(null)"));

            if (intent?.Action == ActionStop)
            {
                global::Android.Util.Log.Info(Tag, "Arrêt du service demandé");
                StopLoop();
                StopForeground(StopForegroundFlags.Remove);
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            StartForeground(ServiceNotifId, BuildServiceNotification());

            if (_loopTask == null || _loopTask.IsCompleted)
            {
                global::Android.Util.Log.Info(Tag, "Démarrage de la boucle de polling");
                _cts = new CancellationTokenSource();
                _loopTask = Task.Run(() => LoopAsync(_cts.Token));
            }

            return StartCommandResult.Sticky;
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            global::Android.Util.Log.Info(Tag, "Boucle de polling démarrée");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var config = _store!.Load();

                    global::Android.Util.Log.Info(Tag,
                        $"Polling : {config.Servers.Count} serveur(s) configuré(s), " +
                        $"intervalle = {config.PollIntervalSeconds}s");

                    foreach (var s in config.Servers)
                    {
                        global::Android.Util.Log.Info(Tag,
                            $"  ? {s.Name} ({s.ApiSummaryUrl}) enabled={s.Enabled}");
                    }

                    int interval = Math.Max(15, config.PollIntervalSeconds);

                    await _engine!.PollAllAsync(config, ct);

                    global::Android.Util.Log.Info(Tag,
                        $"Poll terminé, global={_engine.GlobalState}");

                    await Task.Delay(TimeSpan.FromSeconds(interval), ct);
                }
                catch (TaskCanceledException)
                {
                    global::Android.Util.Log.Info(Tag, "Boucle annulée");
                    break;
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Error(Tag, "Polling error: " + ex);
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                }
            }

            global::Android.Util.Log.Info(Tag, "Boucle de polling terminée");
        }

        private void OnStateChanged(PollResult current, PollResult? previous)
        {
            global::Android.Util.Log.Info(Tag,
                $"État changé : {current.ServerName} {previous?.Status} -> {current.Status}");

            var alert = new PushAlert
            {
                Type = "alert",
                ServerId = current.ServerId,
                ServerName = current.ServerName,
                ServiceType = current.ServiceType,
                BaseUrl = current.BaseUrl,
                Status = current.Status,
                PreviousStatus = previous?.Status ?? "unknown",
                ProblemCount = current.ProblemCount,
                FirstProblem = current.FirstProblem,
                Problems = current.Problems,
                Timestamp = current.Timestamp
            };

            try
            {
                AndroidNotificationService.ShowAlert(this, alert);
                global::Android.Util.Log.Info(Tag, "Notification d'alerte envoyée");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(Tag, "Erreur notif alerte : " + ex);
            }
        }

        private void OnMediaEvent(PollResult server, MediaItem media, string eventType)
        {
            global::Android.Util.Log.Info(Tag,
                $"Événement média : {eventType} '{media.Title}' client='{media.Client}' sur {server.ServerName}");

            var push = new PushMedia
            {
                Type = "media",
                EventType = eventType,
                ServerId = server.ServerId,
                ServerName = server.ServerName,
                ServiceType = server.ServiceType,
                BaseUrl = server.BaseUrl,
                Client = media.Client,
                MediaType = media.Type,
                Title = media.Title,
                Saison = media.Saison,
                Episode = media.Episode,
                Timestamp = DateTime.Now
            };

            try
            {
                AndroidNotificationService.ShowMedia(this, push);
                global::Android.Util.Log.Info(Tag, "Notification média envoyée");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(Tag, "Erreur notif média : " + ex);
            }
        }

        private Notification BuildServiceNotification()
        {
            var intent = new Intent(this, typeof(MainActivity));
            intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            var pending = PendingIntent.GetActivity(
                this, 0, intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            return new NotificationCompat.Builder(this, ServiceChannel)
                .SetContentTitle("MCEMonitor Client")
                .SetContentText("Surveillance des serveurs active")
                .SetSmallIcon(Resource.Drawable.ic_stat_notify)
                .SetContentIntent(pending)
                .SetOngoing(true)
                .SetPriority((int)NotificationPriority.Low)
                .Build();
        }

        private void StopLoop()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
            _loopTask = null;
        }

        public override void OnDestroy()
        {
            global::Android.Util.Log.Info(Tag, "OnDestroy du service");
            StopLoop();
            base.OnDestroy();
        }

        public override IBinder? OnBind(Intent? intent) => null;
        
        internal class AndroidLogListener : System.Diagnostics.TraceListener
        {
            public override void Write(string? message)
            {
                if (message != null)
                    global::Android.Util.Log.Info("MCEMonitor.Poll", message);
            }

            public override void WriteLine(string? message)
            {
                if (message != null)
                    global::Android.Util.Log.Info("MCEMonitor.Poll", message);
            }
        }        
    }
}