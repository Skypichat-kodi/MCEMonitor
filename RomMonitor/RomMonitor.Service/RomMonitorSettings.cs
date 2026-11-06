using System;
using System.IO;
using System.Linq;

namespace RomMonitor.Service
{
    /// <summary>
    /// Charge et sauvegarde la configuration RomMonitor.config
    /// (les paramètres Web sont dans RomMonitor.web.config)
    /// </summary>
    public class RomMonitorSettings
    {
        private static readonly string ConfigFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor"
        );

        private static readonly string ConfigPath    = Path.Combine(ConfigFolder, "RomMonitor.config");
        private static readonly string WebConfigPath = Path.Combine(ConfigFolder, "RomMonitor.web.config");

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
        //  Serveur Web (persistés dans RomMonitor.web.config)
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
                Directory.CreateDirectory(ConfigFolder);

                // 1) Migration one-shot : si *.web.config absent mais que
                //    le *.config contient encore des clés Web → extraire
                MigrateWebConfigIfNeeded();

                // 2) Charger la config principale
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

                // 3) Charger la config Web séparée
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
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Load RomMonitorSettings : " + ex.Message);
            }

            return settings;
        }

        // ------------------------------------------------------------
        //  Migration : extraire les clés Web de RomMonitor.config
        //              vers RomMonitor.web.config (une seule fois)
        // ------------------------------------------------------------
        private static void MigrateWebConfigIfNeeded()
        {
            try
            {
                // Déjà migré ? (ou jamais de config)
                if (File.Exists(WebConfigPath)) return;
                if (!File.Exists(ConfigPath)) return;

                var configLines = File.ReadAllLines(ConfigPath);
                bool hasWebKeys = configLines.Any(l =>
                    l.StartsWith("WebEnabled=", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("WebPort=", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("WebUsername=", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("WebPassword=", StringComparison.OrdinalIgnoreCase));

                if (!hasWebKeys) return;

                CoreLog.Write("[MIGRATION] Extraction des paramètres Web vers RomMonitor.web.config");

                // 1) Extraire les lignes Web
                var webKeys = new[] { "WebEnabled", "WebPort", "WebUsername", "WebPassword" };
                var webLines = configLines
                    .Where(l => webKeys.Any(k => l.StartsWith(k + "=", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                // 2) Écrire le nouveau fichier web.config
                var webFileLines = new[]
                {
                    "# ============================================================",
                    "# RomMonitor.web.config",
                    "# Configuration du serveur Web RomMonitor",
                    "# ============================================================",
                    ""
                }.Concat(webLines).ToArray();

                File.WriteAllLines(WebConfigPath, webFileLines);

                // 3) Nettoyer le config principal (retirer les clés Web + la ligne "# Serveur Web")
                var cleanedLines = configLines
                    .Where(l => !webKeys.Any(k => l.StartsWith(k + "=", StringComparison.OrdinalIgnoreCase)))
                    .Where(l => !l.Trim().Equals("# Serveur Web", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Retirer les lignes vides en trop à la fin
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

        // ------------------------------------------------------------
        //  Sauvegarde config principale
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
                    $"AlertCooldownHours={AlertCooldownHours}"
                };

                File.WriteAllLines(ConfigPath, lines);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Save RomMonitorSettings : " + ex.Message);
            }
        }

        // ------------------------------------------------------------
        //  Sauvegarde config Web
        // ------------------------------------------------------------
        public void SaveWeb()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);

                var lines = new[]
                {
                    "# ============================================================",
                    "# RomMonitor.web.config",
                    "# Configuration du serveur Web RomMonitor",
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
                CoreLog.Write("Erreur SaveWeb RomMonitorSettings : " + ex.Message);
            }
        }

        // ------------------------------------------------------------
        //  Reload à chaud (les 2 fichiers)
        // ------------------------------------------------------------
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
                            case "DiskSpaceWarnPercent":
                                if (int.TryParse(value, out int wp)) DiskSpaceWarnPercent = wp;
                                break;
                            case "DiskSpaceCriticalPercent":
                                if (int.TryParse(value, out int cp)) DiskSpaceCriticalPercent = cp;
                                break;
                            case "DiskSpaceWarnGo":
                                if (int.TryParse(value, out int wg)) DiskSpaceWarnGo = wg;
                                break;
                            case "DiskSpaceCriticalGo":
                                if (int.TryParse(value, out int cg)) DiskSpaceCriticalGo = cg;
                                break;
                            case "AlertOnSmartFailure":
                                if (bool.TryParse(value, out bool asf)) AlertOnSmartFailure = asf;
                                break;
                            case "AlertOnLowDiskSpace":
                                if (bool.TryParse(value, out bool als)) AlertOnLowDiskSpace = als;
                                break;
                            case "AlertCooldownHours":
                                if (int.TryParse(value, out int ach)) AlertCooldownHours = ach;
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
                                if (int.TryParse(value, out int wport)) WebPort = wport;
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

                CoreLog.Write("RomMonitor config rechargée à chaud (2 fichiers)");
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Reload RomMonitorSettings : " + ex.Message);
            }
        }
    }
}