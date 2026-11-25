using System;
using System.Globalization;
using System.IO;

namespace WakeMonitor
{
    /// <summary>
    /// Planification du réveil : à l'heure indiquée, l'exécutable est lancé avec
    /// « --send-wol » et adresse un paquet magique à chaque MAC de la liste.
    ///
    /// Le réglage vit dans WolSchedule.config, au même format clé=valeur que le
    /// reste de la suite. C'est la tâche planifiée Windows qui porte l'heure : ce
    /// fichier dit seulement si l'envoi doit avoir lieu.
    /// </summary>
    public class WolScheduleSettings
    {
        public bool   Enabled { get; set; } = false;
        public string Time    { get; set; } = "07:00";

        public static string Dossier => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor");

        public static string Chemin => Path.Combine(Dossier, "WolSchedule.config");

        public static WolScheduleSettings Load()
        {
            var reglages = new WolScheduleSettings();

            try
            {
                if (!File.Exists(Chemin))
                    return reglages;

                foreach (string ligne in File.ReadAllLines(Chemin))
                {
                    if (string.IsNullOrWhiteSpace(ligne) || ligne.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    var parties = ligne.Split('=', 2);
                    if (parties.Length != 2)
                        continue;

                    string cle     = parties[0].Trim();
                    string valeur  = parties[1].Trim();

                    switch (cle)
                    {
                        case "Enabled":
                            if (bool.TryParse(valeur, out bool actif)) reglages.Enabled = actif;
                            break;
                        case "Time":
                            if (TimeSpan.TryParseExact(valeur, @"hh\:mm", CultureInfo.InvariantCulture, out _))
                                reglages.Time = valeur;
                            break;
                    }
                }
            }
            catch { }

            return reglages;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dossier);

                File.WriteAllLines(Chemin, new[]
                {
                    "# ============================================================",
                    "# WolSchedule.config",
                    "# Réveil programmé des machines dont l'adresse MAC figure",
                    "# dans AllowedWolMacs.txt",
                    "# ============================================================",
                    "",
                    $"Enabled={Enabled.ToString().ToLowerInvariant()}",
                    "",
                    "# Heure d'envoi, au format HH:mm (24 heures)",
                    $"Time={Time}"
                });
            }
            catch { }
        }
    }
}
