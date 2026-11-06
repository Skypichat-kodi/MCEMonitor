using System;
using System.IO;
using System.Linq;
using System.Diagnostics;

namespace SystemMonitor.Service
{
    /// <summary>
    /// Charge et sauvegarde la configuration SystemMonitor.config
    /// (les paramètres Web sont dans SystemMonitor.web.config)
    /// </summary>
    public class SystemMonitorSettings
    {
        private static readonly string ConfigFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor"
        );

        private static readonly string ConfigPath    = Path.Combine(ConfigFolder, "SystemMonitor.config");
        private static readonly string WebConfigPath = Path.Combine(ConfigFolder, "SystemMonitor.web.config");

        // ============================================================
        //  Surveillance
        // ============================================================
        public int Interval { get; set; } = 5;

        // ============================================================
        //  Alertes
        // ============================================================
        public bool AlertOnHighCpu { get; set; } = true;
        public int CpuThresholdPercent { get; set; } = 90;
        public int CpuCooldownMinutes { get; set; } = 15;

        public bool AlertOnHighRam { get; set; } = true;
        public int RamThresholdPercent { get; set; } = 90;

        public bool AlertOnHighTemp { get; set; } = true;
        public int TempThresholdCelsius { get; set; } = 85;

        public bool AlertOnHighGpuTemp { get; set; } = true;
        public int GpuTempThresholdCelsius { get; set; } = 85;

        // ============================================================
        //  Serveur Web (persistés dans SystemMonitor.web.config)
        // ============================================================
        public bool WebEnabled { get; set; } = true;
        public int WebPort { get; set; } = 8083;
        public string WebUsername { get; set; } = "admin";
        public string WebPassword { get; set; } = "changeme";

        // ============================================================
        //  Chargement
        // ============================================================
        public static SystemMonitorSettings Load()
        {
            var settings = new SystemMonitorSettings();

            try
            {
                Directory.CreateDirectory(ConfigFolder);

                MigrateWebConfigIfNeeded();

                // 1) Config principale
                if (!File.Exists(ConfigPath))
                {
                    settings.Save();
                }
                else
                {
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
                            case "Interval":
                                if (int.TryParse(value, out int i)) settings.Interval = i;
                                break;
                            case "AlertOnHighCpu":
                                if (bool.TryParse(value, out bool ac)) settings.AlertOnHighCpu = ac;
                                break;
                            case "CpuThresholdPercent":
                                if (int.TryParse(value, out int ct)) settings.CpuThresholdPercent = ct;
                                break;
                            case "CpuCooldownMinutes":
                                if (int.TryParse(value, out int cc)) settings.CpuCooldownMinutes = cc;
                                break;
                            case "AlertOnHighRam":
                                if (bool.TryParse(value, out bool ar)) settings.AlertOnHighRam = ar;
                                break;
                            case "RamThresholdPercent":
                                if (int.TryParse(value, out int rt)) settings.RamThresholdPercent = rt;
                                break;
                            case "AlertOnHighTemp":
                                if (bool.TryParse(value, out bool at)) settings.AlertOnHighTemp = at;
                                break;
                            case "TempThresholdCelsius":
                                if (int.TryParse(value, out int tt)) settings.TempThresholdCelsius = tt;
                                break;
                            case "AlertOnHighGpuTemp":
                                if (bool.TryParse(value, out bool agt)) settings.AlertOnHighGpuTemp = agt;
                                break;
                            case "GpuTempThresholdCelsius":
                                if (int.TryParse(value, out int gtt)) settings.GpuTempThresholdCelsius = gtt;
                                break;
                        }
                    }
                }

                // Config Web
                if (!File.Exists(WebConfigPath))
                {
                    settings.SaveWeb();
                }
                else
                {
                    foreach (var line in File.ReadAllLines(WebConfigPath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                            continue;

                        var parts = line.Split('=', 2);
                        if (parts.Length != 2) continue;

                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        switch (key)
                        {
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
                
                // Protéger le fichier *.web.config en lecture seule
                EnsureWebConfigProtected();                
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Load SystemMonitorSettings : " + ex.Message);
            }

            return settings;
        }

        // ------------------------------------------------------------
        //  Migration : extraire les clés Web de SystemMonitor.config
        // ------------------------------------------------------------
        private static void MigrateWebConfigIfNeeded()
        {
            try
            {
                if (File.Exists(WebConfigPath)) return;
                if (!File.Exists(ConfigPath)) return;

                var configLines = File.ReadAllLines(ConfigPath);
                bool hasWebKeys = configLines.Any(l =>
                    l.StartsWith("WebEnabled=", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("WebPort=", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("WebUsername=", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("WebPassword=", StringComparison.OrdinalIgnoreCase));

                if (!hasWebKeys) return;

                CoreLog.Write("[MIGRATION] Extraction des paramètres Web vers SystemMonitor.web.config");

                var webKeys = new[] { "WebEnabled", "WebPort", "WebUsername", "WebPassword" };
                var webLines = configLines
                    .Where(l => webKeys.Any(k => l.StartsWith(k + "=", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var webFileLines = new[]
                {
                    "# ============================================================",
                    "# SystemMonitor.web.config",
                    "# Configuration du serveur Web SystemMonitor",
                    "# ============================================================",
                    ""
                }.Concat(webLines).ToArray();

                File.WriteAllLines(WebConfigPath, webFileLines);

                var cleanedLines = configLines
                    .Where(l => !webKeys.Any(k => l.StartsWith(k + "=", StringComparison.OrdinalIgnoreCase)))
                    .Where(l => !l.Trim().Equals("# Serveur Web", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                while (cleanedLines.Count > 0 && string.IsNullOrWhiteSpace(cleanedLines[^1]))
                    cleanedLines.RemoveAt(cleanedLines.Count - 1);

                File.WriteAllLines(ConfigPath, cleanedLines);

                CoreLog.Write("[MIGRATION] OK : " + webLines.Count + " clé(s) Web déplacée(s)");
            }
            catch (Exception ex)
            {
                CoreLog.Write("[MIGRATION] Erreur extraction Web config : " + ex.Message);
            }
        }

        // ============================================================
        //  Sauvegarde config principale
        // ============================================================
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);

                var lines = new[]
                {
                    "# ============================================================",
                    "# SystemMonitor.config",
                    "# Configuration du service SystemMonitor",
                    "# ============================================================",
                    "",
                    "# Fréquence de rafraîchissement (secondes)",
                    $"Interval={Interval}",
                    "",
                    "# Alertes CPU",
                    $"AlertOnHighCpu={AlertOnHighCpu.ToString().ToLower()}",
                    $"CpuThresholdPercent={CpuThresholdPercent}",
                    $"CpuCooldownMinutes={CpuCooldownMinutes}",
                    "",
                    "# Alertes RAM",
                    $"AlertOnHighRam={AlertOnHighRam.ToString().ToLower()}",
                    $"RamThresholdPercent={RamThresholdPercent}",
                    "",
                    "# Alertes Température",
                    $"AlertOnHighTemp={AlertOnHighTemp.ToString().ToLower()}",
                    $"TempThresholdCelsius={TempThresholdCelsius}",
                    "",
                    "# Alertes Température GPU",
                    $"AlertOnHighGpuTemp={AlertOnHighGpuTemp.ToString().ToLower()}",
                    $"GpuTempThresholdCelsius={GpuTempThresholdCelsius}"
                };

                File.WriteAllLines(ConfigPath, lines);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Save SystemMonitorSettings : " + ex.Message);
            }
        }

        // ============================================================
        //  Sauvegarde config Web
        // ============================================================
        public void SaveWeb()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);

                var lines = new[]
                {
                    "# ============================================================",
                    "# SystemMonitor.web.config",
                    "# Configuration du serveur Web SystemMonitor",
                    "# ============================================================",
                    "",
                    $"WebEnabled={WebEnabled.ToString().ToLower()}",
                    $"WebPort={WebPort}",
                    $"WebUsername={WebUsername}",
                    $"WebPassword={WebPassword}"
                };

                File.WriteAllLines(WebConfigPath, lines);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur SaveWeb SystemMonitorSettings : " + ex.Message);
            }
        }

        // ============================================================
        //  Reload à chaud (2 fichiers)
        // ============================================================
        public void Reload()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
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
                            case "Interval":
                                if (int.TryParse(value, out int i)) Interval = i;
                                break;
                            case "AlertOnHighCpu":
                                if (bool.TryParse(value, out bool ac)) AlertOnHighCpu = ac;
                                break;
                            case "CpuThresholdPercent":
                                if (int.TryParse(value, out int ct)) CpuThresholdPercent = ct;
                                break;
                            case "CpuCooldownMinutes":
                                if (int.TryParse(value, out int cc)) CpuCooldownMinutes = cc;
                                break;
                            case "AlertOnHighRam":
                                if (bool.TryParse(value, out bool ar)) AlertOnHighRam = ar;
                                break;
                            case "RamThresholdPercent":
                                if (int.TryParse(value, out int rt)) RamThresholdPercent = rt;
                                break;
                            case "AlertOnHighTemp":
                                if (bool.TryParse(value, out bool at)) AlertOnHighTemp = at;
                                break;
                            case "TempThresholdCelsius":
                                if (int.TryParse(value, out int tt)) TempThresholdCelsius = tt;
                                break;
                            case "AlertOnHighGpuTemp":
                                if (bool.TryParse(value, out bool agt)) AlertOnHighGpuTemp = agt;
                                break;
                            case "GpuTempThresholdCelsius":
                                if (int.TryParse(value, out int gtt)) GpuTempThresholdCelsius = gtt;
                                break;
                        }
                    }
                }

                if (File.Exists(WebConfigPath))
                {
                    foreach (var line in File.ReadAllLines(WebConfigPath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                            continue;

                        var parts = line.Split('=', 2);
                        if (parts.Length != 2) continue;

                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        switch (key)
                        {
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
                }

                CoreLog.Write("Config rechargée à chaud (2 fichiers)");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Reload settings : " + ex.Message);
            }
        }
        
        /// <summary>
        /// Protège le fichier *.web.config en lecture seule pour les utilisateurs
        /// standards, tout en gardant l'écriture pour SYSTEM (le service) et
        /// Administrators (dépannage manuel).
        /// 
        /// Utilise les SID universels (S-1-5-*) pour être indépendant de la
        /// langue de Windows (FR, EN, DE, JP...).
        /// 
        /// Deux étapes obligatoires :
        ///  1. takeown → prendre possession du fichier (sinon icacls = Accès refusé)
        ///  2. icacls  → appliquer les ACL (avec SID universels)
        /// </summary>
        private static void EnsureWebConfigProtected()
        {
            if (!File.Exists(WebConfigPath)) return;

            try
            {
                // 1) Prendre possession du fichier (au groupe Administrateurs via /a)
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

                        if (p.WaitForExit(3000))
                        {
                            _ = takeownOut.Result;
                            _ = takeownErr.Result;
                        }
                    }
                }

                // 2) Appliquer les ACL avec les SID universels
                //    *S-1-5-18     = SYSTEM
                //    *S-1-5-32-544 = Administrateurs
                //    *S-1-5-32-545 = Utilisateurs
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

                // Lire stdout/stderr AVANT WaitForExit pour éviter le deadlock
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
    }
}