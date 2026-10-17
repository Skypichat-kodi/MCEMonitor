using System;
using System.Globalization;
using System.IO;

namespace NetMonitor.Service
{
    public class NetMonitorSettings
    {
        private static readonly string ConfigFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor"
        );

        private static readonly string ConfigPath = Path.Combine(ConfigFolder, "NetMonitor.config");

        // ============================================================
        //  Détecteurs
        // ============================================================
        public int PortScanWindowSeconds { get; set; } = 10;
        public int PortScanThreshold { get; set; } = 20;

        public int BeaconWindowSeconds { get; set; } = 300;
        public int BeaconMinHits { get; set; } = 6;
        public double BeaconJitterTolerancePct { get; set; } = 0.05;
        public bool AutoBlock { get; set; } = false;

        // ============================================================
        //  Serveur Web
        // ============================================================
        public bool WebEnabled { get; set; } = true;
        public int WebPort { get; set; } = 8086;
        public string WebUsername { get; set; } = "admin";
        public string WebPassword { get; set; } = "changeme";

        // ============================================================
        //  Bruit / Faux positifs
        // ============================================================
        public int[] IgnoredRemotePorts { get; set; } = { 53, 123 };

        public string[] IgnoredProcesses { get; set; } = new[]
        {
            "svchost", "System", "Idle", "CrossDeviceService", "OneDrive",
            "OneDriveSetup", "MicrosoftEdgeUpdate", "MicrosoftEdgeUpdateSetup",
            "WidgetService", "MoUsoCoreWorker", "UsoClient", "WaasMedicAgent",
            "TrustedInstaller", "SearchIndexer", "SearchProtocolHost", "SearchFilterHost",
            "backgroundTaskHost", "RuntimeBroker", "ShellExperienceHost",
            "StartMenuExperienceHost", "Widgets", "MsMpEng", "NisSrv",
            "SecurityHealthService", "WpnService", "WpnUserService"
        };

        public string[] IgnoredNetworks { get; set; } = new[]
        {
            "127.0.0.0/8",
            "10.0.0.0/8",
            "172.16.0.0/12",
            "192.168.0.0/16"
        };

        /// <summary>
        /// Mode Debug : désactive toutes les whitelists pour tester les détecteurs.
        /// À utiliser avec précaution (risque d'inonder le système d'alertes).
        /// </summary>
        public bool DisableFilters { get; set; } = false;

        // ============================================================
        //  Chargement
        // ============================================================
        public static NetMonitorSettings Load()
        {
            var settings = new NetMonitorSettings();

            try
            {
                if (!File.Exists(ConfigPath))
                {
                    settings.Save();
                    return settings;
                }

                foreach (var line in File.ReadAllLines(ConfigPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    switch (key)
                    {
                        case "PortScanWindowSeconds":
                            if (int.TryParse(value, out int psw)) settings.PortScanWindowSeconds = psw;
                            break;
                        case "PortScanThreshold":
                            if (int.TryParse(value, out int pst)) settings.PortScanThreshold = pst;
                            break;
                        case "BeaconWindowSeconds":
                            if (int.TryParse(value, out int bw)) settings.BeaconWindowSeconds = bw;
                            break;
                        case "BeaconMinHits":
                            if (int.TryParse(value, out int bh)) settings.BeaconMinHits = bh;
                            break;
                        case "BeaconJitterTolerancePct":
                            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bj))
                                settings.BeaconJitterTolerancePct = bj;
                            break;
                        case "AutoBlock":
                            if (bool.TryParse(value, out bool ab)) settings.AutoBlock = ab;
                            break;
                        case "IgnoredProcesses":
                            settings.IgnoredProcesses = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            break;
                        case "IgnoredNetworks":
                            settings.IgnoredNetworks = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            break;
                        case "DisableFilters":
                            if (bool.TryParse(value, out bool df)) settings.DisableFilters = df;
                            break;

                        case "WebEnabled":
                            if (bool.TryParse(value, out bool we)) settings.WebEnabled = we;
                            break;
                        case "WebPort":
                            if (int.TryParse(value, out int wp)) settings.WebPort = wp;
                            break;
                        case "WebUsername":
                            settings.WebUsername = value;
                            break;
                        case "WebPassword":
                            settings.WebPassword = value;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Load NetMonitorSettings : " + ex.Message);
            }

            return settings;
        }

        // ============================================================
        //  Sauvegarde
        // ============================================================
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);

                var lines = new[]
                {
                    "# ============================================================",
                    "# NetMonitor.config",
                    "# Configuration du service NetMonitor",
                    "# ============================================================",
                    "",
                    "# --- Détecteur : Scan de ports ---",
                    $"PortScanWindowSeconds={PortScanWindowSeconds}",
                    $"PortScanThreshold={PortScanThreshold}",
                    "",
                    "# --- Détecteur : Beaconing ---",
                    $"BeaconWindowSeconds={BeaconWindowSeconds}",
                    $"BeaconMinHits={BeaconMinHits}",
                    $"BeaconJitterTolerancePct={BeaconJitterTolerancePct.ToString(CultureInfo.InvariantCulture)}",
                    "",
                    "# --- Processus ignorés (séparés par virgule) ---",
                    $"IgnoredProcesses={string.Join(",", IgnoredProcesses)}",
                    "",
                    "# --- Réseaux ignorés / locaux (CIDR séparés par virgule) ---",
                    $"IgnoredNetworks={string.Join(",", IgnoredNetworks)}",
                    "",
                    "# --- Blocage automatique ---",
                    $"AutoBlock={AutoBlock.ToString().ToLower()}",
                    "",
                    "# --- Mode Debug ---",
                    $"DisableFilters={DisableFilters.ToString().ToLower()}",
                    "",
                    "# --- Serveur Web ---",
                    $"WebEnabled={WebEnabled.ToString().ToLower()}",
                    $"WebPort={WebPort}",
                    $"WebUsername={WebUsername}",
                    $"WebPassword={WebPassword}"
                };

                File.WriteAllLines(ConfigPath, lines);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Save NetMonitorSettings : " + ex.Message);
            }
        }

        // ============================================================
        //  Rechargement à chaud
        // ============================================================
        public void Reload()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;

                foreach (var line in File.ReadAllLines(ConfigPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    switch (key)
                    {
                        case "PortScanWindowSeconds":
                            if (int.TryParse(value, out int psw)) PortScanWindowSeconds = psw;
                            break;
                        case "PortScanThreshold":
                            if (int.TryParse(value, out int pst)) PortScanThreshold = pst;
                            break;
                        case "BeaconWindowSeconds":
                            if (int.TryParse(value, out int bw)) BeaconWindowSeconds = bw;
                            break;
                        case "BeaconMinHits":
                            if (int.TryParse(value, out int bh)) BeaconMinHits = bh;
                            break;
                        case "BeaconJitterTolerancePct":
                            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bj))
                                BeaconJitterTolerancePct = bj;
                            break;
                        case "AutoBlock":
                            if (bool.TryParse(value, out bool ab)) AutoBlock = ab;
                            break;
                        case "IgnoredProcesses":
                            IgnoredProcesses = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            break;
                        case "IgnoredNetworks":
                            IgnoredNetworks = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            break;
                        case "DisableFilters":
                            if (bool.TryParse(value, out bool df)) DisableFilters = df;
                            break;

                        case "WebEnabled":
                            if (bool.TryParse(value, out bool we)) WebEnabled = we;
                            break;
                        case "WebPort":
                            if (int.TryParse(value, out int wp)) WebPort = wp;
                            break;
                        case "WebUsername":
                            WebUsername = value;
                            break;
                        case "WebPassword":
                            WebPassword = value;
                            break;
                    }
                }

                CoreLog.Write("Config NetMonitor rechargée à chaud");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Reload NetMonitorSettings : " + ex.Message);
            }
        }
    }
}