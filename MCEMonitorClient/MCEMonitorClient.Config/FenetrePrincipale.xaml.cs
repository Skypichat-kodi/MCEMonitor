using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using MCEMonitor.Languages;

namespace MCEMonitorClient.Config
{
    /// <summary>
    /// Fenêtre principale du client de configuration.
    ///
    /// Étape 1 de la migration : la disposition reprend celle de l'ancien MainForm
    /// (900 x 680, non redimensionnable) au style des interfaces MCEMonitor. Les
    /// actions ne sont pas encore branchées sur les services — elles le seront à
    /// l'étape suivante, avec la parité de l'ancien formulaire comme référence.
    /// </summary>
    public partial class FenetrePrincipale : Window
    {
        public FenetrePrincipale()
        {
            InitializeComponent();

            // Libellé posé ici : la clé contient une apostrophe, que la syntaxe
            // {loc:Tr '...'} du XAML ne peut pas porter.
            CaseNotifier.Content = LanguageManager.Get("Notifier les changements d'état")
                                   ?? "Notifier les changements d'état";

            TexteVersion.Text = "v" + VersionAffichable();
        }

        /// <summary>Version de l'assembly, sans le suffixe « +sha » ajouté par GitVersion.</summary>
        private static string VersionAffichable()
        {
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "";

            int plusIdx = version.IndexOf('+');
            return plusIdx >= 0 ? version.Substring(0, plusIdx) : version;
        }

        // ============================================================
        //  Barre de titre : déplacement et boutons
        // ============================================================

        private void EnTete_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { /* bouton relâché avant le déplacement */ }
            }
        }

        private void SurReduire(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void SurFermer(object sender, RoutedEventArgs e) => Close();

        // ============================================================
        //  Actions — à brancher à l'étape suivante
        //  Chacune correspond à une fonction de l'ancien MainForm.cs.old :
        //  AddServer, EditSelected, ExportConfig, ImportConfig, DeleteSelected,
        //  ShowServersStateForm, ToggleAutomaticTask, ToggleServiceManual, SaveAll.
        // ============================================================

        private void SurAjouter(object sender, RoutedEventArgs e)      { /* AddServer */ }
        private void SurModifier(object sender, RoutedEventArgs e)     { /* EditSelected */ }
        private void SurExporter(object sender, RoutedEventArgs e)     { /* ExportConfig */ }
        private void SurImporter(object sender, RoutedEventArgs e)     { /* ImportConfig */ }
        private void SurSupprimer(object sender, RoutedEventArgs e)    { /* DeleteSelected */ }
        private void SurEtatsServeurs(object sender, RoutedEventArgs e){ /* ShowServersStateForm */ }
        private void SurAutomatique(object sender, RoutedEventArgs e)  { /* ToggleAutomaticTask */ }
        private void SurService(object sender, RoutedEventArgs e)      { /* ToggleServiceManual */ }
        private void SurEnregistrer(object sender, RoutedEventArgs e)  { /* SaveAll */ }
    }
}
