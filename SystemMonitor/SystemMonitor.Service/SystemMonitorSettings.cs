using System;
using System.IO;

namespace SystemMonitor.Service
{
    /// <summary>
    /// Charge et sauvegarde la configuration SystemMonitor.config
    /// </summary>
    public class SystemMonitorSettings
    {
        private static readonly string ConfigFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor"
        );

        private static readonly string ConfigPath = Path.Combine(ConfigFolder, "SystemMonitor.config");

        // ============================================================
        //  Surveillance
        // ============================================================
        public int Interval { get; set; } = 5;   // en secondes

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

        // ============================================================
        //  Serveur Web
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
                    if (parts.Length != 2)
                        continue;

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
                CoreLog.Write("Erreur Load SystemMonitorSettings : " + ex.Message);
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
                    "# Serveur Web",
                    $"WebEnabled={WebEnabled.ToString().ToLower()}",
                    $"WebPort={WebPort}",
                    $"WebUsername={WebUsername}",
                    $"WebPassword={WebPassword}"
                };

                File.WriteAllLines(ConfigPath, lines);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Save SystemMonitorSettings : " + ex.Message);
            }
        }
    }
}