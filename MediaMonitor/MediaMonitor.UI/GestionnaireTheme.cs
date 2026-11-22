using System;
using System.IO;
using System.Windows;

namespace MediaMonitor.UI
{
    /// <summary>
    /// Applique le thème choisi dans MCEMonitor.
    ///
    /// Le choix est lu dans `%ProgramData%\MCEMonitor\theme-ui.config`, le même fichier
    /// que l'application principale : changer de thème depuis MCEMonitor vaut donc pour
    /// toutes les interfaces qui l'ont adopté.
    ///
    /// Le chargement se fait au démarrage, avant la création de la fenêtre. Les styles
    /// référencent les pinceaux en StaticResource, résolu une seule fois : le thème doit
    /// être présent dans les ressources fusionnées avant eux.
    /// </summary>
    internal static class GestionnaireTheme
    {
        public const string Sombre = "sombre";
        public const string Clair  = "clair";

        private static readonly string CheminConfig = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "theme-ui.config");

        /// <summary>Thème enregistré par MCEMonitor, « sombre » par défaut.</summary>
        public static string ThemeChoisi()
        {
            try
            {
                if (File.Exists(CheminConfig))
                {
                    string valeur = File.ReadAllText(CheminConfig).Trim().ToLowerInvariant();

                    if (valeur == Clair || valeur == Sombre)
                        return valeur;
                }
            }
            catch
            {
                // Fichier illisible : on garde le thème sombre.
            }

            return Sombre;
        }

        public static void Appliquer(Application application)
        {
            string theme = ThemeChoisi() == Clair ? "Clair" : "Sombre";

            application.Resources.MergedDictionaries.Insert(0, new ResourceDictionary
            {
                Source = new Uri($"Themes/{theme}.xaml", UriKind.Relative)
            });
        }
    }
}
