using System;
using System.Globalization;
using System.IO;
using System.Threading;
using MCEMonitor.Languages;
using NetMonitor.Service;

namespace NetMonitor.Service
{
    internal static class Program
    {
        private static Mutex _mutex;
        private static NetMonitorEngine _engine;
        private static ServiceIpcServer _ipc;
        private static Web.MiniHttpServer _webServer;
        
        static void Main(string[] args)
        {
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MCEMonitor_NetMonitor", out createdNew);

            if (!createdNew)
            {
                CoreLog.Write("NetMonitor.Service déjà en cours. Sortie.");
                return;
            }

            NetMonitor.Service.Web.PeerStatusService.StartBackgroundRefresh("NetMonitor");
            
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
            CoreLog.Write($"=== NetMonitor.Service démarré (langue = {selectedLang}) ===");

            // ============================================================
            //  Configuration
            // ============================================================
            var settings = NetMonitorSettings.Load();
            CoreLog.Write($"Config : PortScan seuil={settings.PortScanThreshold}, Beaconing fenêtre={settings.BeaconWindowSeconds}s");

            // ============================================================
            //  Moteur ETW
            // ============================================================
            _engine = new NetMonitorEngine(settings);
            _engine.LoadBlockedIps();
            _engine.LoadBlockedIpRetries();
            _engine.LoadHistory();
            _engine.LoadAudit();
            _engine.Start();

            CoreLog.Write($"Scanners whitelistés : {string.Join(", ", settings.TrustedScannerPatterns)}");
            
            // ============================================================
            //  IPC
            // ============================================================
            _ipc = new ServiceIpcServer(_engine, settings);
            _ipc.Start();
            _webServer = new Web.MiniHttpServer(_engine, settings);
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

                var settings = NetMonitorSettings.Load();

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