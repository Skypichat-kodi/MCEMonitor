using System;
using System.IO;

namespace RomMonitor.Service
{
    /// <summary>
    /// Charge et sauvegarde la configuration RomMonitor.config
    /// </summary>
    public class RomMonitorSettings
    {
        private static readonly string ConfigFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor"
        );

        private static readonly string ConfigPath = Path.Combine(ConfigFolder, "RomMonitor.config");

        // ------------------------------------------------------------
        //  Paramètres existants
        // ------------------------------------------------------------
        public int Interval { get; set; } = 15;

        public int DiskSpaceWarnPercent { get; set; } = 15;
        public int DiskSpaceCriticalPercent { get; set; } = 5;

        public int DiskSpaceWarnGo { get; set; } = 10;
        public int DiskSpaceCriticalGo { get; set; } = 5;

        public bool AlertOnSmartFailure { get; set; } = true;
        public bool AlertOnLowDiskSpace { get; set; } = true;

        public int AlertCooldownHours { get; set; } = 24;

        // ------------------------------------------------------------
        //  ?? Serveur Web
        // ------------------------------------------------------------
        public bool WebEnabled { get; set; } = true;
        public int WebPort { get; set; } = 8085;
        public string WebUsername { get; set; } = "admin";
        public string WebPassword { get; set; } = "changeme";

        // ------------------------------------------------------------
        //  Chargement
        // ------------------------------------------------------------
        public static RomMonitorSettings Load()
        {
            var settings = new RomMonitorSettings();

            try
            {
                if (!File.Exists(ConfigPath))
                {
                    settings.Save();
                    return settings;
                }

                var lines = File.ReadAllLines(ConfigPath);

                foreach (var line in lines)
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

                        case "DiskSpaceWarnPercent":
                            if (int.TryParse(value, out int wp)) settings.DiskSpaceWarnPercent = wp;
                            break;

                        case "DiskSpaceCriticalPercent":
                            if (int.TryParse(value, out int cp)) settings.DiskSpaceCriticalPercent = cp;
                            break;

                        case "DiskSpaceWarnGo":
                            if (int.TryParse(value, out int wg)) settings.DiskSpaceWarnGo = wg;
                            break;

                        case "DiskSpaceCriticalGo":
                            if (int.TryParse(value, out int cg)) settings.DiskSpaceCriticalGo = cg;
                            break;

                        case "AlertOnSmartFailure":
                            if (bool.TryParse(value, out bool asf)) settings.AlertOnSmartFailure = asf;
                            break;

                        case "AlertOnLowDiskSpace":
                            if (bool.TryParse(value, out bool als)) settings.AlertOnLowDiskSpace = als;
                            break;

                        case "AlertCooldownHours":
                            if (int.TryParse(value, out int ach)) settings.AlertCooldownHours = ach;
                            break;

                        case "WebEnabled":
                            if (bool.TryParse(value, out bool we)) settings.WebEnabled = we;
                            break;

                        case "WebPort":
                            if (int.TryParse(value, out int wport)) settings.WebPort = wport;
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
                CoreLog.Write("Erreur Load RomMonitorSettings : " + ex.Message);
            }

            return settings;
        }

        // ------------------------------------------------------------
        //  Sauvegarde
        // ------------------------------------------------------------
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);

                var lines = new[]
                {
                    "# ============================================================",
                    "# RomMonitor.config",
                    "# Configuration du service RomMonitor",
                    "# ============================================================",
                    "",
                    "# Fréquence de contrôle (minutes)",
                    $"Interval={Interval}",
                    "",
                    "# Seuil d'alerte espace disque (% libre)",
                    $"DiskSpaceWarnPercent={DiskSpaceWarnPercent}",
                    $"DiskSpaceCriticalPercent={DiskSpaceCriticalPercent}",
                    "",
                    "# Seuil d'alerte espace disque (Go libre)",
                    $"DiskSpaceWarnGo={DiskSpaceWarnGo}",
                    $"DiskSpaceCriticalGo={DiskSpaceCriticalGo}",
                    "",
                    "# Alertes email",
                    $"AlertOnSmartFailure={AlertOnSmartFailure.ToString().ToLower()}",
                    $"AlertOnLowDiskSpace={AlertOnLowDiskSpace.ToString().ToLower()}",
                    "",
                    "# Anti-spam : délai minimum entre 2 alertes email du même type (heures)",
                    $"AlertCooldownHours={AlertCooldownHours}",
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
                CoreLog.Write("Erreur Save RomMonitorSettings : " + ex.Message);
            }
        }
    }
}