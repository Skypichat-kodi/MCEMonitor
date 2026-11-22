using System;
using System.IO;
using System.Windows;

namespace MCEMonitor.UI
{
    /// <summary>
    /// Choix et application du thème de l'application.
    ///
    /// Le thème est un simple dictionnaire de ressources (Themes/Sombre.xaml ou
    /// Themes/Clair.xaml) qui définit les seize clés de couleur. Les styles, eux,
    /// ne contiennent aucune couleur littérale : ils pointent sur ces clés.
    ///
    /// Le choix est enregistré dans `theme-ui.config`, distinct du `theme.config` de
    /// l'ancienne interface WinForms qui contenait un nom de palette Krypton.
    ///
    /// Le chargement se fait au démarrage, avant la première fenêtre. Les styles
    /// utilisent StaticResource, résolu une seule fois : changer de thème demande
    /// donc un redémarrage. C'est assumé — le choix se fait une fois pour toutes —
    /// et cela évite de convertir cinq mille lignes de XAML en DynamicResource.
    /// </summary>
    internal static class GestionnaireTheme
    {
        public const string Sombre = "sombre";
        public const string Clair  = "clair";

        private static readonly string CheminConfig = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "theme-ui.config");

        /// <summary>Thème enregistré, « sombre » par défaut.</summary>
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

        /// <summary>Enregistre le choix. Le changement prend effet au prochain démarrage.</summary>
        public static bool Enregistrer(string theme)
        {
            try
            {
                string? dossier = Path.GetDirectoryName(CheminConfig);

                if (!string.IsNullOrEmpty(dossier))
                    Directory.CreateDirectory(dossier);

                File.WriteAllText(CheminConfig, theme);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Charge le thème puis les styles, dans cet ordre, dans les ressources de
        /// l'application. À appeler avant la création de la première fenêtre.
        /// </summary>
        public static void Appliquer(Application application)
        {
            string theme = ThemeChoisi() == Clair ? "Clair" : "Sombre";

            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"Themes/{theme}.xaml", UriKind.Relative)
            });

            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("Themes/Styles.xaml", UriKind.Relative)
            });
        }
    }
}
