using System;
using System.IO;

namespace RomMonitor.Service
{
    /// <summary>
    /// Charge et sauvegarde la configuration RomMonitor.config
    /// </summary>
    public class RomMonitorSettings
    {
        // ------------------------------------------------------------
        //  Chemins
        // ------------------------------------------------------------
        private static readonly string ConfigFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor"
        );

        private static readonly string ConfigPath = Path.Combine(ConfigFolder, "RomMonitor.config");

        // ------------------------------------------------------------
        //  Paramètres
        // ------------------------------------------------------------
        public int Interval { get; set; } = 15;

        public int DiskSpaceWarnPercent { get; set; } = 15;
        public int DiskSpaceCriticalPercent { get; set; } = 5;

        public int DiskSpaceWarnGo { get; set; } = 20;
        public int DiskSpaceCriticalGo { get; set; } = 5;

        public bool AlertOnSmartFailure { get; set; } = true;
        public bool AlertOnLowDiskSpace { get; set; } = false;

        public int AlertCooldownHours { get; set; } = 24;

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
                    // Créer le fichier par défaut
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
                    "# Fréquence de vérification (minutes)",
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
                    $"AlertCooldownHours={AlertCooldownHours}"
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