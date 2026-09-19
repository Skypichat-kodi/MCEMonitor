using System;
using System.Threading;

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

            // Init logs
            CoreLog.Clear();
            CoreLog.Write("=== RomMonitor.Service démarré ===");

            // Charger config
            var settings = RomMonitorSettings.Load();
            CoreLog.Write($"Config : Interval={settings.Interval}min, AlertOnSmartFailure={settings.AlertOnSmartFailure}");

            // Démarrer engine
            _engine = new RomMonitorEngine(settings);
            _engine.Start();

            // Démarrer IPC
            _ipc = new ServiceIpcServer(_engine, settings);
            _ipc.Start();

            // Ajouter la règle pare-feu AVANT de démarrer le WebServer
            if (settings.WebEnabled)
            {
                FirewallHelper.UpdateFirewallRule(settings.WebPort);
            }

            // Démarrer WebServer
            _webServer = new RomMonitor.Service.Web.MiniHttpServer(_engine, settings);
            _webServer.Start();

            CoreLog.Write("Service en attente (Thread.Sleep Infinite).");
            Thread.Sleep(Timeout.Infinite);
        }

        // ============================================================
        //  Redémarrage du WebServer (appelé par IPC)
        // ============================================================
        internal static void RestartWebServer()
        {
            try
            {
                _webServer?.Stop();
                Thread.Sleep(500);

                var settings = RomMonitorSettings.Load();

                // S'assurer que le pare-feu autorise le port actuel
                if (settings.WebEnabled)
                {
                    FirewallHelper.UpdateFirewallRule(settings.WebPort);
                }

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