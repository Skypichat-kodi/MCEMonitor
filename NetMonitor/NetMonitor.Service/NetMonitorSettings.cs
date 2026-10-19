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
        private static readonly string WebConfigPath = Path.Combine(ConfigFolder, "NetMonitor.Web.config");

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
        //  Recherche d'IP (lien externe vers un service d'analyse)
        // ============================================================
        public string IpLookupUrl { get; set; } = "https://www.abuseipdb.com/check/{ip}";

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
        public string[] IgnoredNetworks { get; set; } = new[] { "127.0.0.0/8", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" };
        public bool DisableFilters { get; set; } = false;
        public int[] SuspiciousRemotePorts { get; set; } = { 4444, 1337, 31337, 6667, 3389 };
        public int BlacklistLevel { get; set; } = 3;
        public DateTime BlacklistLastUpdate { get; set; } = DateTime.MinValue;

        // ============================================================
        //  Chargement
        // ============================================================
        public static NetMonitorSettings Load()
        {
            var settings = new NetMonitorSettings();

            try
            {
                if (File.Exists(ConfigPath))
                {
                    foreach (var line in File.ReadAllLines(ConfigPath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                        var parts = line.Split('=', 2);
                        if (parts.Length != 2) continue;

                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        switch (key)
                        {
                            case "PortScanWindowSeconds": if (int.TryParse(value, out int psw)) settings.PortScanWindowSeconds = psw; break;
                            case "PortScanThreshold": if (int.TryParse(value, out int pst)) settings.PortScanThreshold = pst; break;
                            case "BeaconWindowSeconds": if (int.TryParse(value, out int bw)) settings.BeaconWindowSeconds = bw; break;
                            case "BeaconMinHits": if (int.TryParse(value, out int bh)) settings.BeaconMinHits = bh; break;
                            case "BeaconJitterTolerancePct": if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bj)) settings.BeaconJitterTolerancePct = bj; break;
                            case "AutoBlock": if (bool.TryParse(value, out bool ab)) settings.AutoBlock = ab; break;
                            case "IgnoredProcesses": settings.IgnoredProcesses = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                            case "IgnoredNetworks": settings.IgnoredNetworks = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                            case "DisableFilters": if (bool.TryParse(value, out bool df)) settings.DisableFilters = df; break;
                            case "SuspiciousRemotePorts": if (value.Contains(',')) { var ports = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); if (ports.Length > 0) settings.SuspiciousRemotePorts = Array.ConvertAll(ports, int.Parse); } break;
                            case "BlacklistLevel": if (int.TryParse(value, out int bl)) settings.BlacklistLevel = bl; break;
                            case "BlacklistLastUpdate": if (DateTime.TryParse(value, out DateTime blu)) settings.BlacklistLastUpdate = blu; break;
                            case "IpLookupUrl": if (!string.IsNullOrWhiteSpace(value)) settings.IpLookupUrl = value; break;
                        }
                    }
                }

                if (File.Exists(WebConfigPath))
                {
                    foreach (var line in File.ReadAllLines(WebConfigPath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                        var parts = line.Split('=', 2);
                        if (parts.Length != 2) continue;

                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        switch (key)
                        {
                            case "WebEnabled": if (bool.TryParse(value, out bool we)) settings.WebEnabled = we; break;
                            case "WebPort": if (int.TryParse(value, out int wp)) settings.WebPort = wp; break;
                            case "WebUsername": settings.WebUsername = value; break;
                            case "WebPassword": settings.WebPassword = value; break;
                        }
                    }
                }
                else
                {
                    settings.SaveWeb();
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Load NetMonitorSettings : " + ex.Message);
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);
                var lines = new[]
                {
                    "# ============================================================",
                    "# NetMonitor.config",
                    "# Configuration du service NetMonitor (Détecteurs)",
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
                    "# --- Processus ignorés ---",
                    $"IgnoredProcesses={string.Join(",", IgnoredProcesses)}",
                    "",
                    "# --- Réseaux ignorés ---",
                    $"IgnoredNetworks={string.Join(",", IgnoredNetworks)}",
                    "",
                    "# --- Blocage automatique ---",
                    $"AutoBlock={AutoBlock.ToString().ToLower()}",
                    "",
                    "# --- Mode Debug ---",
                    $"DisableFilters={DisableFilters.ToString().ToLower()}",
                    "",
                    "# --- Détecteurs de périmètre ---",
                    $"SuspiciousRemotePorts={string.Join(",", SuspiciousRemotePorts)}",
                    "",
                    "# --- Threat Intelligence ---",
                    $"BlacklistLevel={BlacklistLevel}",
                    $"BlacklistLastUpdate={BlacklistLastUpdate:O}",
                    "",
                    "# --- Recherche d'IP (lien externe) ---",
                    $"IpLookupUrl={IpLookupUrl}"
                };
                File.WriteAllLines(ConfigPath, lines);
            }
            catch (Exception ex) { CoreLog.Write("Erreur Save NetMonitorSettings : " + ex.Message); }
        }

        public void SaveWeb()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);
                var lines = new[]
                {
                    "# ============================================================",
                    "# NetMonitor.Web.config",
                    "# Configuration du serveur Web de NetMonitor",
                    "# ============================================================",
                    "",
                    $"WebEnabled={WebEnabled.ToString().ToLower()}",
                    $"WebPort={WebPort}",
                    $"WebUsername={WebUsername}",
                    $"WebPassword={WebPassword}"
                };
                File.WriteAllLines(WebConfigPath, lines);
            }
            catch (Exception ex) { CoreLog.Write("Erreur SaveWeb NetMonitorSettings : " + ex.Message); }
        }

        public void Reload()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    foreach (var line in File.ReadAllLines(ConfigPath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                        var parts = line.Split('=', 2);
                        if (parts.Length != 2) continue;
                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        switch (key)
                        {
                            case "PortScanWindowSeconds": if (int.TryParse(value, out int psw)) PortScanWindowSeconds = psw; break;
                            case "PortScanThreshold": if (int.TryParse(value, out int pst)) PortScanThreshold = pst; break;
                            case "BeaconWindowSeconds": if (int.TryParse(value, out int bw)) BeaconWindowSeconds = bw; break;
                            case "BeaconMinHits": if (int.TryParse(value, out int bh)) BeaconMinHits = bh; break;
                            case "BeaconJitterTolerancePct": if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bj)) BeaconJitterTolerancePct = bj; break;
                            case "AutoBlock": if (bool.TryParse(value, out bool ab)) AutoBlock = ab; break;
                            case "IgnoredProcesses": IgnoredProcesses = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                            case "IgnoredNetworks": IgnoredNetworks = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                            case "DisableFilters": if (bool.TryParse(value, out bool df)) DisableFilters = df; break;
                            case "SuspiciousRemotePorts": if (value.Contains(',')) { var ports = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); if (ports.Length > 0) SuspiciousRemotePorts = Array.ConvertAll(ports, int.Parse); } break;
                            case "BlacklistLevel": if (int.TryParse(value, out int bl)) BlacklistLevel = bl; break;
                            case "BlacklistLastUpdate": if (DateTime.TryParse(value, out DateTime blu)) BlacklistLastUpdate = blu; break;
                            case "IpLookupUrl": if (!string.IsNullOrWhiteSpace(value)) IpLookupUrl = value; break;
                        }
                    }
                }

                if (File.Exists(WebConfigPath))
                {
                    foreach (var line in File.ReadAllLines(WebConfigPath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                        var parts = line.Split('=', 2);
                        if (parts.Length != 2) continue;
                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        switch (key)
                        {
                            case "WebEnabled": if (bool.TryParse(value, out bool we)) WebEnabled = we; break;
                            case "WebPort": if (int.TryParse(value, out int wp)) WebPort = wp; break;
                            case "WebUsername": WebUsername = value; break;
                            case "WebPassword": WebPassword = value; break;
                        }
                    }
                }
                CoreLog.Write("Config NetMonitor rechargée à chaud");
            }
            catch (Exception ex) { CoreLog.Write("Erreur Reload NetMonitorSettings : " + ex.Message); }
        }
    }
}