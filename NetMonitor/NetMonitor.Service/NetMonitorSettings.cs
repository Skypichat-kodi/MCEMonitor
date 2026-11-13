using System;
using System.Globalization;
using System.IO;
using System.Diagnostics;

namespace NetMonitor.Service
{
    public class NetMonitorSettings
    {
        private static readonly string ConfigFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor"
        );

        private static readonly string ConfigPath = Path.Combine(ConfigFolder, "NetMonitor.config");
        private static readonly string WebConfigPath = Path.Combine(ConfigFolder, "NetMonitor.web.config");

        // ============================================================
        //  Détecteurs
        // ============================================================
        public int PortScanWindowSeconds            { get; set; } = 10;
        public int PortScanThreshold                { get; set; } = 20;
        public int BeaconWindowSeconds              { get; set; } = 300;
        public int BeaconMinHits                    { get; set; } = 6;
        public int  MicrosoftIpRangeRefreshHours    { get; set; } = 24;
        public double BeaconJitterTolerancePct      { get; set; } = 0.05;
        public bool EnableProcessTrust              { get; set; } = true;
        public bool AutoBlock                       { get; set; } = false;
        public bool AutoBlockCritical               { get; set; } = true;
        public bool AutoBlockWarning                { get; set; } = true;
        public bool AutoBlockSubnet                 { get; set; } = false;
        public bool MicrosoftIpRangeEnabled         { get; set; } = true;
        public bool IgnoreSearchScanners            { get; set; } = false;
        
        // ============================================================
        //  Robots d'exploration (Google, Bing...)
        // ============================================================
        public bool   GoogleIpRangeEnabled          { get; set; } = true;
        public int    GoogleIpRangeRefreshHours     { get; set; } = 24;
        public bool   BlockBots                     { get; set; } = false;   // Robots officiels
        public bool   BlockCrawlers                 { get; set; } = true;    // Robots génériques

        // Patterns des robots génériques (Yandex, Baidu...)
        public string[] CrawlerPatterns             { get; set; } = new[]
        {
            "yandex.com", "yandex.ru", "baidu.com", "bytespider",
            "semrush.com", "ahrefs.com", "mj12bot", "dotbot",
            "petalbot", "gptbot", "oai-searchbot", "ccbot"
        };

        // ============================================================
        //  Serveur Web
        // ============================================================
        public bool WebEnabled                      { get; set; } = true;
        public int WebPort                          { get; set; } = 8086;
        public string WebUsername                   { get; set; } = "admin";
        public string WebPassword                   { get; set; } = "changeme";

        // ============================================================
        //  OTP (One-Time Password) — vérification par email
        // ============================================================
        public bool OtpEnabled                      { get; set; } = false;
        public int  OtpSessionMinutes               { get; set; } = 15;
        public int  OtpCodeLength                   { get; set; } = 6;

        // ============================================================
        //  Recherche d'IP (lien externe vers un service d'analyse)
        // ============================================================
        public string IpLookupUrl                   { get; set; } = "https://www.abuseipdb.com/check/{ip}";

        // ============================================================
        //  Géolocalisation IP
        // ============================================================
        public bool GeoIpEnabled                    { get; set; } = true;
        public string GeoIpProvider                 { get; set; } = "https://ipwho.is/{ip}";
        public bool ReverseDnsEnabled               { get; set; } = true;

        // ============================================================
        //  Sévérité — patterns de classification (reverse DNS / ISP / Org)
        // ============================================================
        public string[] CriticalPatterns            { get; set; } = new[]
        {
            "infrawat.ch", "bulsat.com", "serverastra.com", "axarnet.es"
        };

        public string[] ScannerPatterns { get; set; } = new[]
        {
            "shadowserver.org", "censys.io", "shodan.io", "onyphe.com",
            "binaryedge.io", "internet-measurement.com", "rapid7.com", "stretchoid.com"
        };

        public string[] SafePatterns { get; set; } = new[]
        {
            "microsoft.com", "google.com", "cloudflare.com",
            "amazonaws.com", "akamai.net", "apple.com"
        };

        public string[] WhitelistPatterns { get; set; } = Array.Empty<string>();
        public int SeverityVersion { get; set; } = 0;

        // ============================================================
        //  Bruit / Faux positifs
        // ============================================================
        public int[] IgnoredRemotePorts     { get; set; } = { 53, 123 };
        public string[] IgnoredProcesses    { get; set; } = new[]
        {
            "svchost", "System", "Idle", "CrossDeviceService", "OneDrive",
            "OneDriveSetup", "MicrosoftEdgeUpdate", "MicrosoftEdgeUpdateSetup",
            "WidgetService", "MoUsoCoreWorker", "UsoClient", "WaasMedicAgent",
            "TrustedInstaller", "SearchIndexer", "SearchProtocolHost", "SearchFilterHost",
            "backgroundTaskHost", "RuntimeBroker", "ShellExperienceHost",
            "StartMenuExperienceHost", "Widgets", "MsMpEng", "NisSrv",
            "SecurityHealthService", "WpnService", "WpnUserService"
        };
        public string[] IgnoredNetworks     { get; set; } = new[] { "127.0.0.0/8", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" };
        public bool DisableFilters          { get; set; } = false;
        public int[] SuspiciousRemotePorts  { get; set; } = { 4444, 1337, 31337, 6667, 3389 };
        public int BlacklistLevel           { get; set; } = 3;
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
                            case "PortScanWindowSeconds"        : if (int.TryParse(value, out int psw)) settings.PortScanWindowSeconds = psw; break;
                            case "PortScanThreshold"            : if (int.TryParse(value, out int pst)) settings.PortScanThreshold = pst; break;
                            case "BeaconWindowSeconds"          : if (int.TryParse(value, out int bw)) settings.BeaconWindowSeconds = bw; break;
                            case "BeaconMinHits"                : if (int.TryParse(value, out int bh)) settings.BeaconMinHits = bh; break;
                            case "BeaconJitterTolerancePct"     : if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bj)) settings.BeaconJitterTolerancePct = bj; break;
                            case "DisableFilters"               : if (bool.TryParse(value, out bool df)) settings.DisableFilters = df; break;
                            case "SuspiciousRemotePorts"        : if (value.Contains(',')) { var ports = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); if (ports.Length > 0) settings.SuspiciousRemotePorts = Array.ConvertAll(ports, int.Parse); } break;
                            case "BlacklistLevel"               : if (int.TryParse(value, out int bl)) settings.BlacklistLevel = bl; break;
                            case "BlacklistLastUpdate"          : if (DateTime.TryParse(value, out DateTime blu)) settings.BlacklistLastUpdate = blu; break;
                            case "IpLookupUrl"                  : if (!string.IsNullOrWhiteSpace(value)) settings.IpLookupUrl = value; break;
                            case "GeoIpEnabled"                 : if (bool.TryParse(value, out bool gi)) settings.GeoIpEnabled = gi; break;
                            case "GeoIpProvider"                : if (!string.IsNullOrWhiteSpace(value)) settings.GeoIpProvider = value; break;
                            case "ReverseDnsEnabled"            : if (bool.TryParse(value, out bool rd)) settings.ReverseDnsEnabled = rd; break;
                            case "EnableProcessTrust"           : if (bool.TryParse(value, out bool ept)) settings.EnableProcessTrust = ept; break;
                            case "IgnoreSearchScanners"         : if (bool.TryParse(value, out bool iss)) settings.IgnoreSearchScanners = iss; break;
                            case "IgnoredProcesses"             : settings.IgnoredProcesses = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                            case "IgnoredNetworks"              : settings.IgnoredNetworks = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                            case "TrustedScannerPatterns":
                            case "ScannerPatterns":
                                var patterns = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                if (patterns.Length > 0) settings.ScannerPatterns = patterns;
                                break;
                            case "CriticalPatterns"             : settings.CriticalPatterns = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                            case "SafePatterns"                 : settings.SafePatterns = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                            case "WhitelistPatterns"            : settings.WhitelistPatterns = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                            case "SeverityVersion"              : if (int.TryParse(value, out int sv)) settings.SeverityVersion = sv; break;
                            case "AutoBlock"                    : if (bool.TryParse(value, out bool ab)) settings.AutoBlock = ab; break;
                            case "AutoBlockCritical"            : if (bool.TryParse(value, out bool abc)) settings.AutoBlockCritical = abc; break;
                            case "AutoBlockWarning"             : if (bool.TryParse(value, out bool abw)) settings.AutoBlockWarning = abw; break;
                            case "AutoBlockSubnet"              : if (bool.TryParse(value, out bool abs)) settings.AutoBlockSubnet = abs; break;
                            case "MicrosoftIpRangeEnabled"      : if (bool.TryParse(value, out bool mire)) settings.MicrosoftIpRangeEnabled = mire; break;
                            case "MicrosoftIpRangeRefreshHours" : if (int.TryParse(value, out int mirr)) settings.MicrosoftIpRangeRefreshHours = mirr; break;
                            case "GoogleIpRangeEnabled"         : if (bool.TryParse(value, out bool gire)) settings.GoogleIpRangeEnabled = gire; break;
                            case "GoogleIpRangeRefreshHours"    : if (int.TryParse(value, out int girr)) settings.GoogleIpRangeRefreshHours = girr; break;
                            case "BlockBots"                    : if (bool.TryParse(value, out bool bb)) settings.BlockBots = bb; break;
                            case "BlockCrawlers"                : if (bool.TryParse(value, out bool bc)) settings.BlockCrawlers = bc; break;
                            case "CrawlerPatterns"              : settings.CrawlerPatterns = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;                            
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
                            case "WebEnabled"        : if (bool.TryParse(value, out bool we)) settings.WebEnabled = we; break;
                            case "WebPort"           : if (int.TryParse(value, out int wp)) settings.WebPort = wp; break;
                            case "WebUsername"       : settings.WebUsername = value; break;
                            case "WebPassword"       : settings.WebPassword = value; break;
                            case "OtpEnabled"        : if (bool.TryParse(value, out bool oe)) settings.OtpEnabled = oe; break;
                            case "OtpSessionMinutes" : if (int.TryParse(value, out int osm)) settings.OtpSessionMinutes = osm; break;
                            case "OtpCodeLength"     : if (int.TryParse(value, out int ocl)) settings.OtpCodeLength = ocl; break;
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
                    $"IpLookupUrl={IpLookupUrl}",
                    "",
                    "# --- Géolocalisation IP ---",
                    $"GeoIpEnabled={GeoIpEnabled.ToString().ToLower()}",
                    $"GeoIpProvider={GeoIpProvider}",
                    $"ReverseDnsEnabled={ReverseDnsEnabled.ToString().ToLower()}",
                    "",
                    "# ============================================================",
                    "#  Sévérité — patterns de classification (reverse DNS / ISP / Org)",
                    "# ============================================================",
                    "# Vraiment malveillants (rouge)",
                    $"CriticalPatterns={string.Join(",", CriticalPatterns)}",
                    "",
                    "# Scanners de recherche connus (vert moyen)",
                    $"ScannerPatterns={JoinOrFallback(ScannerPatterns, DefaultScannerPatterns)}",
                    "",
                    "# Domaines légitimes connus (vert clair)",
                    $"SafePatterns={string.Join(",", SafePatterns)}",
                    "",
                    "# Whitelist utilisateur (vert foncé) — vide par défaut",
                    $"WhitelistPatterns={string.Join(",", WhitelistPatterns)}",
                    "",
                    "# Version de la logique de sévérité (0 = legacy, 2 = SeverityScorer)",
                    $"SeverityVersion={SeverityVersion}",
                    "",
                    "# --- Whitelist de processus (chemin + hash + signature) ---",
                    $"EnableProcessTrust={EnableProcessTrust.ToString().ToLower()}",
                    "",
                    "# --- Ignorer les scanners de recherche (Shodan, Censys...) ---",
                    $"IgnoreSearchScanners={IgnoreSearchScanners.ToString().ToLower()}",
                    "",
                    $"AutoBlock={AutoBlock.ToString().ToLower()}",
                    $"AutoBlockCritical={AutoBlockCritical.ToString().ToLower()}",
                    $"AutoBlockWarning={AutoBlockWarning.ToString().ToLower()}",
                    $"AutoBlockSubnet={AutoBlockSubnet.ToString().ToLower()}",
                    "",
                    "# --- Robots d'exploration (Google, Bing...) ---",
                    $"GoogleIpRangeEnabled={GoogleIpRangeEnabled.ToString().ToLower()}",
                    $"GoogleIpRangeRefreshHours={GoogleIpRangeRefreshHours}",
                    $"BlockBots={BlockBots.ToString().ToLower()}",
                    $"BlockCrawlers={BlockCrawlers.ToString().ToLower()}",
                    $"CrawlerPatterns={string.Join(",", CrawlerPatterns)}",
                    "",
                    "# --- Fichier Microsoft IP Ranges ---",
                    $"MicrosoftIpRangeEnabled={MicrosoftIpRangeEnabled.ToString().ToLower()}",
                    $"MicrosoftIpRangeRefreshHours={MicrosoftIpRangeRefreshHours}",
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
                    "# NetMonitor.web.config",
                    "# Configuration du serveur Web de NetMonitor",
                    "# ============================================================",
                    "",
                    $"WebEnabled={WebEnabled.ToString().ToLower()}",
                    $"WebPort={WebPort}",
                    $"WebUsername={WebUsername}",
                    $"WebPassword={WebPassword}",
                    "",
                    "# ============================================================",
                    "# OTP (One-Time Password) — vérification par email",
                    "# ============================================================",
                    $"OtpEnabled={OtpEnabled.ToString().ToLower()}",
                    $"OtpSessionMinutes={OtpSessionMinutes}",
                    $"OtpCodeLength={OtpCodeLength}"
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
                            case "PortScanWindowSeconds"        : if (int.TryParse(value, out int psw)) PortScanWindowSeconds = psw; break;
                            case "PortScanThreshold"            : if (int.TryParse(value, out int pst)) PortScanThreshold = pst; break;
                            case "BeaconWindowSeconds"          : if (int.TryParse(value, out int bw)) BeaconWindowSeconds = bw; break;
                            case "BeaconMinHits"                : if (int.TryParse(value, out int bh)) BeaconMinHits = bh; break;
                            case "BeaconJitterTolerancePct"     : if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bj)) BeaconJitterTolerancePct = bj; break;
                            case "SeverityVersion"              : if (int.TryParse(value, out int sv)) SeverityVersion = sv; break;
                            case "AutoBlock"                    : if (bool.TryParse(value, out bool ab)) AutoBlock = ab; break;
                            case "AutoBlockCritical"            : if (bool.TryParse(value, out bool abc)) AutoBlockCritical = abc; break;
                            case "AutoBlockWarning"             : if (bool.TryParse(value, out bool abw)) AutoBlockWarning = abw; break;
                            case "AutoBlockSubnet"              : if (bool.TryParse(value, out bool abs)) AutoBlockSubnet = abs; break;
                            case "MicrosoftIpRangeEnabled"      : if (bool.TryParse(value, out bool mire2)) MicrosoftIpRangeEnabled = mire2; break;
                            case "MicrosoftIpRangeRefreshHours" : if (int.TryParse(value, out int mirr2)) MicrosoftIpRangeRefreshHours = mirr2; break;
                            case "GoogleIpRangeEnabled"         : if (bool.TryParse(value, out bool gire3)) GoogleIpRangeEnabled = gire3; break;
                            case "GoogleIpRangeRefreshHours"    : if (int.TryParse(value, out int girr3)) GoogleIpRangeRefreshHours = girr3; break;
                            case "BlockBots"                    : if (bool.TryParse(value, out bool bb3)) BlockBots = bb3; break;
                            case "BlockCrawlers"                : if (bool.TryParse(value, out bool bc3)) BlockCrawlers = bc3; break;
                            case "CrawlerPatterns"              : CrawlerPatterns = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;                            
                            case "IgnoredProcesses"             : IgnoredProcesses = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                            case "IgnoredNetworks"              : IgnoredNetworks = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                            case "DisableFilters"               : if (bool.TryParse(value, out bool df)) DisableFilters = df; break;
                            case "SuspiciousRemotePorts"        : if (value.Contains(',')) { var ports = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); if (ports.Length > 0) SuspiciousRemotePorts = Array.ConvertAll(ports, int.Parse); } break;
                            case "BlacklistLevel"               : if (int.TryParse(value, out int bl)) BlacklistLevel = bl; break;
                            case "BlacklistLastUpdate"          : if (DateTime.TryParse(value, out DateTime blu)) BlacklistLastUpdate = blu; break;
                            case "IpLookupUrl"                  : if (!string.IsNullOrWhiteSpace(value)) IpLookupUrl = value; break;
                            case "GeoIpEnabled"                 : if (bool.TryParse(value, out bool gi)) GeoIpEnabled = gi; break;
                            case "GeoIpProvider"                : if (!string.IsNullOrWhiteSpace(value)) GeoIpProvider = value; break;
                            case "ReverseDnsEnabled"            : if (bool.TryParse(value, out bool rd)) ReverseDnsEnabled = rd; break;
                            case "TrustedScannerPatterns":
                            case "ScannerPatterns":
                                var patterns = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                if (patterns.Length > 0) ScannerPatterns = patterns;
                                break;
                            case "CriticalPatterns"             : CriticalPatterns = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                            case "SafePatterns"                 : SafePatterns = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                            case "WhitelistPatterns"            : WhitelistPatterns = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
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
                            case "WebEnabled"        : if (bool.TryParse(value, out bool we)) WebEnabled = we; break;
                            case "WebPort"           : if (int.TryParse(value, out int wp)) WebPort = wp; break;
                            case "WebUsername"       : WebUsername = value; break;
                            case "WebPassword"       : WebPassword = value; break;
                            case "OtpEnabled"        : if (bool.TryParse(value, out bool oe)) OtpEnabled = oe; break;
                            case "OtpSessionMinutes" : if (int.TryParse(value, out int osm)) OtpSessionMinutes = osm; break;
                            case "OtpCodeLength"     : if (int.TryParse(value, out int ocl)) OtpCodeLength = ocl; break;
                        }
                    }
                }

                EnsureWebConfigProtected();
                CoreLog.Write("Config NetMonitor rechargée à chaud");
            }
            catch (Exception ex) { CoreLog.Write("Erreur Reload NetMonitorSettings : " + ex.Message); }
        }

        /// <summary>
        /// Ajoute un pattern à une des listes de classification, puis sauvegarde.
        /// Liste = "safe", "critical", "scanner", "crawler" ou "whitelist".
        /// </summary>
        public bool AddPattern(string list, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();

            switch (list.Trim().ToLowerInvariant())
            {
                case "safe":
                    if (!SafePatterns.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase)))
                        SafePatterns = SafePatterns.Append(value).ToArray();
                    break;
                case "critical":
                    if (!CriticalPatterns.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase)))
                        CriticalPatterns = CriticalPatterns.Append(value).ToArray();
                    break;
                case "scanner":
                    if (!ScannerPatterns.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase)))
                        ScannerPatterns = ScannerPatterns.Append(value).ToArray();
                    break;
                case "crawler":
                    if (!CrawlerPatterns.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase)))
                        CrawlerPatterns = CrawlerPatterns.Append(value).ToArray();
                    break;
                case "whitelist":
                    if (!WhitelistPatterns.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase)))
                        WhitelistPatterns = WhitelistPatterns.Append(value).ToArray();
                    break;
                default:
                    return false;
            }

            Save();
            CoreLog.Info($"[PATTERN] '{value}' ajouté à la liste '{list}'");
            return true;
        }

        private static void EnsureWebConfigProtected()
        {
            if (!File.Exists(WebConfigPath)) return;

            try
            {
                var takeownPsi = new ProcessStartInfo("takeown",
                    $"/f \"{WebConfigPath}\" /a")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var p = Process.Start(takeownPsi))
                {
                    if (p != null)
                    {
                        var takeownOut = p.StandardOutput.ReadToEndAsync();
                        var takeownErr = p.StandardError.ReadToEndAsync();
                        if (p.WaitForExit(3000)) { _ = takeownOut.Result; _ = takeownErr.Result; }
                    }
                }

                var icaclsPsi = new ProcessStartInfo("icacls",
                    $"\"{WebConfigPath}\" /inheritance:r " +
                    $"/grant \"*S-1-5-18:(F)\" " +
                    $"/grant \"*S-1-5-32-544:(F)\" " +
                    $"/grant \"*S-1-5-32-545:(R)\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p2 = Process.Start(icaclsPsi);
                if (p2 == null) return;

                var outTask = p2.StandardOutput.ReadToEndAsync();
                var errTask = p2.StandardError.ReadToEndAsync();

                if (!p2.WaitForExit(5000))
                {
                    try { p2.Kill(true); } catch { }
                    CoreLog.Write($"[PROTECT] {Path.GetFileName(WebConfigPath)} : timeout (5s)");
                    return;
                }

                if (p2.ExitCode == 0)
                    CoreLog.Write($"[PROTECT] {Path.GetFileName(WebConfigPath)} protégé en lecture seule");
                else
                    CoreLog.Write($"[PROTECT] {Path.GetFileName(WebConfigPath)} : échec (code {p2.ExitCode}) — {errTask.Result}");
            }
            catch (Exception ex)
            {
                CoreLog.Write($"[PROTECT] Erreur : {ex.Message}");
            }
        }

        private static string JoinOrFallback(string[] values, string[] fallback)
            => string.Join(",", values.Length > 0 ? values : fallback);

        private static readonly string[] DefaultScannerPatterns = new[]
        {
            "shadowserver.org", "censys.io", "shodan.io", "onyphe.com",
            "binaryedge.io", "internet-measurement.com", "rapid7.com", "stretchoid.com"
        };
    }
}