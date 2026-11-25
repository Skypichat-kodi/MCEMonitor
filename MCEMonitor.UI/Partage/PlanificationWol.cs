using System;
using System.Globalization;
using System.IO;

namespace MCEMonitor.UI
{
    /// <summary>
    /// Lecture et écriture de WolSchedule.config, le réglage du réveil programmé.
    ///
    /// Le fichier est partagé avec WakeMonitor.exe, qui le relit de son côté : le
    /// module est indépendant, la petite classe est donc dupliquée ici plutôt que
    /// référencée depuis l'exécutable.
    /// </summary>
    internal static class PlanificationWol
    {
        public static string Dossier => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor");

        public static string Chemin => Path.Combine(Dossier, "WolSchedule.config");

        public static (bool Actif, string Heure) Lire()
        {
            bool actif = false;
            string heure = "07:00";

            try
            {
                if (!File.Exists(Chemin))
                    return (actif, heure);

                foreach (string ligne in File.ReadAllLines(Chemin))
                {
                    if (string.IsNullOrWhiteSpace(ligne) || ligne.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    var parties = ligne.Split('=', 2);
                    if (parties.Length != 2)
                        continue;

                    string cle    = parties[0].Trim();
                    string valeur = parties[1].Trim();

                    if (cle == "Enabled" && bool.TryParse(valeur, out bool b))
                        actif = b;

                    if (cle == "Time" && TimeSpan.TryParseExact(valeur, @"hh\:mm", CultureInfo.InvariantCulture, out _))
                        heure = valeur;
                }
            }
            catch { }

            return (actif, heure);
        }

        public static void Ecrire(bool actif, string heure)
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
                $"Enabled={actif.ToString().ToLowerInvariant()}",
                "",
                "# Heure d'envoi, au format HH:mm (24 heures)",
                $"Time={heure}"
            });
        }
    }
}
