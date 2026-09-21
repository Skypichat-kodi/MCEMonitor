using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using MCEMonitor.Languages;

namespace RomMonitor.Service
{
    internal static class Program
    {
        private static Mutex _mutex;
        private static RomMonitorEngine _engine;
        private static ServiceIpcServer _ipc;
        private static RomMonitor.Service.Web.MiniHttpServer _webServer;

        static void Main(string[] args)
        {
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_RomMonitor", out createdNew);

            if (!createdNew)
            {
                CoreLog.Write("RomMonitor.Service déjà en cours. Sortie.");
                return;
            }

            // ============================================================
            //  LANGUE : argument -lang xx-XX > fichier language.config > défaut
            // ============================================================
            string selectedLang = null;

            // 1) Argument -lang fr-FR / en-GB
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("-lang", StringComparison.OrdinalIgnoreCase))
                {
                    selectedLang = args[i + 1];
                    break;
                }
            }

            // 2) Sinon : fichier écrit par MCEMonitor.exe
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

            // 3) Fallback
            if (string.IsNullOrEmpty(selectedLang))
                selectedLang = "fr-FR";

            Thread.CurrentThread.CurrentUICulture = new CultureInfo(selectedLang);
            Thread.CurrentThread.CurrentCulture  = new CultureInfo(selectedLang);
            LanguageManager.Load(selectedLang);

            // ============================================================
            //  Logs
            // ============================================================
            CoreLog.Clear();
            CoreLog.Write($"=== RomMonitor.Service démarré (langue = {selectedLang}) ===");

            var settings = RomMonitorSettings.Load();
            CoreLog.Write($"Config : Interval={settings.Interval}min, AlertOnSmartFailure={settings.AlertOnSmartFailure}");

            _engine = new RomMonitorEngine(settings);
            _engine.Start();

            _ipc = new ServiceIpcServer(_engine, settings);
            _ipc.Start();

            if (settings.WebEnabled)
                FirewallHelper.UpdateFirewallRule(settings.WebPort);

            _webServer = new RomMonitor.Service.Web.MiniHttpServer(_engine, settings);
            _webServer.Start();

            CoreLog.Write("Service en attente (Thread.Sleep Infinite).");
            Thread.Sleep(Timeout.Infinite);
        }

        internal static void RestartWebServer()
        {
            try
            {
                _webServer?.Stop();
                Thread.Sleep(500);

                var settings = RomMonitorSettings.Load();

                if (settings.WebEnabled)
                    FirewallHelper.UpdateFirewallRule(settings.WebPort);

                _webServer = new RomMonitor.Service.Web.MiniHttpServer(_engine, settings);
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