using System;
using System.Globalization;
using System.IO;
using System.Threading;
using MCEMonitor.Languages;

namespace SystemMonitor.Service
{
    internal static class Program
    {
        private static Mutex _mutex;
        private static SystemMonitorEngine _engine;
        private static ServiceIpcServer _ipc;
        private static Web.MiniHttpServer _webServer;

        static void Main(string[] args)
        {
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_SystemMonitor", out createdNew);

            if (!createdNew)
            {
                CoreLog.Write("SystemMonitor.Service déjà en cours. Sortie.");
                return;
            }

            // ============================================================
            //  LANGUE
            // ============================================================
            string selectedLang = null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("-lang", StringComparison.OrdinalIgnoreCase))
                {
                    selectedLang = args[i + 1];
                    break;
                }
            }

            if (string.IsNullOrEmpty(selectedLang))
            {
                string cfgPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "language.config"
                );

                if (File.Exists(cfgPath))
                    selectedLang = File.ReadAllText(cfgPath).Trim();
            }

            if (string.IsNullOrEmpty(selectedLang))
                selectedLang = "fr-FR";

            Thread.CurrentThread.CurrentUICulture = new CultureInfo(selectedLang);
            Thread.CurrentThread.CurrentCulture = new CultureInfo(selectedLang);
            LanguageManager.Load(selectedLang);

            // ============================================================
            //  Logs
            // ============================================================
            CoreLog.Clear();
            CoreLog.Write($"=== SystemMonitor.Service démarré (langue = {selectedLang}) ===");

            // ============================================================
            //  Configuration
            // ============================================================
            var settings = SystemMonitorSettings.Load();
            CoreLog.Write($"Config : Interval={settings.Interval}s, WebEnabled={settings.WebEnabled}");

            // ============================================================
            //  Moteur de collecte
            // ============================================================
            _engine = new SystemMonitorEngine(settings);
            _engine.Start();

            // ============================================================
            //  IPC
            // ============================================================
            _ipc = new ServiceIpcServer(_engine, settings);
            _ipc.Start();

            // ============================================================
            //  Pare-feu
            // ============================================================
            if (settings.WebEnabled)
                FirewallHelper.UpdateFirewallRule(settings.WebPort);

            // ============================================================
            //  Web
            // ============================================================
            _webServer = new Web.MiniHttpServer(_engine, settings);
            _webServer.Start();

            CoreLog.Write("Service en attente (Thread.Sleep Infinite).");
            Thread.Sleep(Timeout.Infinite);
        }

        /// <summary>
        /// Redémarre le serveur web (appelé par IPC après changement de config).
        /// </summary>
        internal static void RestartWebServer()
        {
            try
            {
                _webServer?.Stop();
                Thread.Sleep(500);

                var settings = SystemMonitorSettings.Load();

                if (settings.WebEnabled)
                    FirewallHelper.UpdateFirewallRule(settings.WebPort);

                _webServer = new Web.MiniHttpServer(_engine, settings);
                _webServer.Start();

                CoreLog.Write("WebServer redémarré");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur RestartWebServer : " + ex.Message);
            }
        }
    }
}