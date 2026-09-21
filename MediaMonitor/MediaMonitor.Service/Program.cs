using System;
using System.IO;
using System.Text;
using System.Threading;
using MCEMonitor.Languages;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Service;

namespace MediaMonitor.Service
{
    internal static class Program
    {
        // ---------------------------------------------------------------
        //  État partagé avec les autres classes
        // ---------------------------------------------------------------
        internal static MediaMonitorEngine Engine { get; private set; }

        private static string _lastReportStatus = "[CODE02]|AUCUN";
        internal static string LastReportStatus
        {
            get => _lastReportStatus;
            set => _lastReportStatus = value;
        }

        private static Mutex _mutex;
        private static WebServer _webServer;
        private static ReportScheduler _reportScheduler;
        private static BackupService _backupService;
        private static ConfigWatcher _configWatcher;

        // ---------------------------------------------------------------
        //  Entrée du programme
        // ---------------------------------------------------------------
        static void Main()
        {
            // ============================================================
            //  Langue
            // ============================================================
            string selectedLang = "fr-FR";
            string[] args = Environment.GetCommandLineArgs();

            int idx = Array.IndexOf(args, "-lang");
            if (idx >= 0 && idx < args.Length - 1)
                selectedLang = args[idx + 1];

            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(selectedLang);
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(selectedLang);
            LanguageManager.Load(selectedLang);

            // ============================================================
            //  Mutex global
            // ============================================================
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_Service", out createdNew);
            if (!createdNew)
                return;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // ============================================================
            //  Init logs
            // ============================================================
            CoreLog.IsLoggingEnabled = () => ServiceIpcServer.ServiceLoggingEnabled;
            CoreLog.Clear();
            ScheduleLogger.Clear();
            CoreLog.Write("=== MediaMonitor.Service démarré (SYSTEM) ===");

            // ============================================================
            //  Engine
            // ============================================================
            Engine = new MediaMonitorEngine();

            var cfg = WebServerSettings.Load();
            Engine.DvbViewerUrl = cfg.DvbViewerUrl;
            Engine.DvbViewerUser = cfg.DvbViewerUser;
            Engine.DvbViewerPass = cfg.DvbViewerPass;
            Engine.DvbViewerEnabled = cfg.DvbViewerSwitch;

            CoreLog.Write("DVBViewer RS initial: " + Engine.DvbViewerEnabled);

            try
            {
                Engine.Start();
                CoreLog.Write("Engine.Start() exécuté.");
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR Engine.Start() : " + ex);
            }

            _lastReportStatus = "[CODE02]|AUCUN";
            ScheduleLogger.Write(_lastReportStatus);

            // ============================================================
            //  IPC + WebServer
            // ============================================================
            try
            {
                var ipc = new ServiceIpcServer(Engine);
                ipc.Start();
                CoreLog.Write("IPC Server démarré.");

                StartWebServerIfEnabled();
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR IPC Start : " + ex);
            }

            // ============================================================
            //  Email setting
            // ============================================================
            ServiceIpcServer.EmailSendingEnabled = ConfigLoader.LoadEmailEnabled();
            CoreLog.Write("EmailSendingEnabled = " + ServiceIpcServer.EmailSendingEnabled);

            // ============================================================
            //  Backup
            // ============================================================
            _backupService = new BackupService(Engine);
            _backupService.Start();

            // ============================================================
            //  Watchers
            // ============================================================
            _configWatcher = new ConfigWatcher(_backupService, () => _reportScheduler?.OnShutdownConfigChanged());
            _configWatcher.SetDvbViewerState(Engine.DvbViewerEnabled);
            _configWatcher.Start();

            // ============================================================
            //  Scheduler
            // ============================================================
            _reportScheduler = new ReportScheduler(Engine);
            _reportScheduler.Start();

            // ============================================================
            //  Power events
            // ============================================================
            Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) =>
            {
                if (e.Mode == Microsoft.Win32.PowerModes.Resume)
                {
                    _backupService.NotifyWakeUp();
                    _reportScheduler?.OnWakeUp();
                }
            };

            CoreLog.Write("Service en attente (Thread.Sleep Infinite).");
            Thread.Sleep(Timeout.Infinite);
        }

        // ---------------------------------------------------------------
        //  WebServer
        // ---------------------------------------------------------------
        internal static void StartWebServerIfEnabled()
        {
            try
            {
                var settings = WebServerSettings.Load();

                if (!settings.Enabled)
                {
                    CoreLog.Write("Serveur Web désactivé (Enabled=false).");
                    return;
                }

                _webServer = new WebServer(settings.Port, Engine);
                _webServer.Start();

                CoreLog.Write($"Serveur Web démarré sur le port {settings.Port}.");
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR StartWebServerIfEnabled : " + ex);
            }
        }

        internal static void StopWebServer()
        {
            try
            {
                _webServer?.Stop();
                _webServer = null;
            }
            catch (Exception ex)
            {
                CoreLog.Write("ERREUR StopWebServer : " + ex);
            }
        }

        // ---------------------------------------------------------------
        //  Wrappers pour compatibilité (appelés depuis IpcCommandHandler)
        // ---------------------------------------------------------------
        internal static void WriteScheduleLog(string message) => ScheduleLogger.Write(message);
        internal static void ClearScheduleLog() => ScheduleLogger.Clear();
        internal static (int hour, int minute)? LoadShutdownTime() => ConfigLoader.LoadShutdownTime();
        internal static void SaveBackup(MediaMonitorEngine engine) => _backupService?.SaveBackup();
        internal static void RestartBackupTimer() => _backupService?.Restart();
    }
}