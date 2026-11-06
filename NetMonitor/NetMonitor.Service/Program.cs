using System;
using System.Globalization;
using System.IO;
using System.Threading;
using MCEMonitor.Languages;
using NetMonitor.Service;
using NetMonitor.Service.Web;

namespace NetMonitor.Service
{
    internal static class Program
    {
        private static Mutex?              _mutex;                       
        private static NetMonitorEngine    _engine        = null!;       
        private static ServiceIpcServer?   _ipc;                         
        private static MiniHttpServer?     _webServer;                  

        /// <summary>
        /// Nettoie un éventuel service WinDivert orphelin avant le démarrage du moteur.
        /// Corrige l'erreur 1058 sur les machines où une clé de registre
        /// "Start=4" (DISABLED) est restée après une désinstallation incomplète.
        /// </summary>
        private static void EnsureWinDivertClean()
        {
            try
            {
                // -- 1. Vérifier si la clé de registre existe encore --
                bool serviceExists = false;
                try
                {
                    using var regKey = Microsoft.Win32.Registry.LocalMachine
                        .OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WinDivert");

                    serviceExists = regKey != null;
                }
                catch { /* ignore */ }

                if (!serviceExists)
                {
                    CoreLog.Write("[WINDIVERT] Aucun service orphelin détecté");
                    return;
                }

                CoreLog.Write("[WINDIVERT] Service orphelin détecté ? nettoyage");

                // -- 2. Tenter sc delete (ignorera l'erreur si déjà supprimé) --
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("sc", "delete WinDivert")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var proc = System.Diagnostics.Process.Start(psi);
                    proc?.WaitForExit(3000);
                }
                catch { /* ignore */ }

                // -- 3. Attendre que Windows finalise (crucial) --
                Thread.Sleep(1500);

                // -- 4. Vérifier que la clé a bien disparu --
                try
                {
                    using var regKey = Microsoft.Win32.Registry.LocalMachine
                        .OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WinDivert");

                    if (regKey != null)
                    {
                        // La clé existe toujours ? suppression directe en dernier recours
                        CoreLog.Write("[WINDIVERT] Clé de registre persistante ? suppression directe");
                        Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(
                            @"SYSTEM\CurrentControlSet\Services\WinDivert",
                            throwOnMissingSubKey: false);
                        Thread.Sleep(500);
                    }

                    CoreLog.Write("[WINDIVERT] Nettoyage terminé");
                }
                catch (Exception ex)
                {
                    CoreLog.Write("[WINDIVERT] Erreur suppression registre : " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("[WINDIVERT] Erreur EnsureWinDivertClean : " + ex.Message);
            }
        }
        
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
            string? selectedLang = null;  

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
            
            EnsureWinDivertClean();
            
            _engine.Start();

            CoreLog.Write($"Scanners whitelistés : {string.Join(", ", settings.ScannerPatterns)}");
            
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