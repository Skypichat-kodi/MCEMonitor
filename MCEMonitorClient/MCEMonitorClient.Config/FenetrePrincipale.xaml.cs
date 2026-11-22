using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using MCEMonitor.Languages;
using MCEMonitorClient.Config.Models;
using MCEMonitorClient.Config.Services;

namespace MCEMonitorClient.Config
{
    /// <summary>
    /// Fenêtre principale du client de configuration.
    ///
    /// La disposition reprend celle de l'ancien MainForm (900 x 680, non
    /// redimensionnable) au style des interfaces MCEMonitor.
    ///
    /// État de la migration : la configuration est branchée (lecture du fichier,
    /// grille des serveurs, options, enregistrement). Restent à brancher les
    /// actions sur les serveurs et la gestion du service.
    /// </summary>
    public partial class FenetrePrincipale : Window
    {
        /// <summary>Bornes de l'intervalle de polling, celles de l'ancien NumericUpDown.</summary>
        private const int IntervalleMin = 5;
        private const int IntervalleMax = 600;

        private readonly ServersConfig _config;

        public FenetrePrincipale()
        {
            InitializeComponent();

            // Libellé posé ici : la clé contient une apostrophe, que la syntaxe
            // {loc:Tr '...'} du XAML ne peut pas porter.
            CaseNotifier.Content = LanguageManager.Get("Notifier les changements d'état")
                                   ?? "Notifier les changements d'état";

            TexteVersion.Text = "v" + VersionAffichable();

            // Configuration : mêmes appels que l'ancien formulaire.
            _config = ServerConfigStore.Load();

            GrilleServeurs.ItemsSource = _config.Servers;

            AfficherOptions();

            TexteStatut.Text = string.Format(
                LanguageManager.Get("Fichier : {0}") ?? "Fichier : {0}",
                ServerConfigStore.GetConfigPath());
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
        //  Configuration : affichage et enregistrement
        // ============================================================

        /// <summary>
        /// Reporte dans les champs les valeurs chargées depuis le fichier.
        /// Correspond à LoadOptions + RefreshGrid de l'ancien MainForm.
        /// </summary>
        private void AfficherOptions()
        {
            int intervalle = Math.Clamp(_config.PollIntervalSeconds, IntervalleMin, IntervalleMax);

            ChampIntervalle.Text    = intervalle.ToString(CultureInfo.CurrentCulture);
            CaseNotifier.IsChecked  = _config.NotifyOnStateChange;
            CaseSon.IsChecked       = _config.PlaySoundOnCritical;

            // La liste des serveurs n'est pas observable : on force le redessin.
            GrilleServeurs.Items.Refresh();
        }

        /// <summary>Correspond à SaveAll de l'ancien MainForm.</summary>
        private void SurEnregistrer(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(ChampIntervalle.Text, NumberStyles.Integer,
                              CultureInfo.CurrentCulture, out int intervalle)
                || intervalle < IntervalleMin || intervalle > IntervalleMax)
            {
                MessageBox.Show(this,
                    LanguageManager.Get("L'intervalle doit être compris entre 5 et 600 secondes.")
                        ?? "L'intervalle doit être compris entre 5 et 600 secondes.",
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                ChampIntervalle.Focus();
                ChampIntervalle.SelectAll();
                return;
            }

            _config.PollIntervalSeconds = intervalle;
            _config.NotifyOnStateChange = CaseNotifier.IsChecked == true;
            _config.PlaySoundOnCritical = CaseSon.IsChecked == true;

            if (ServerConfigStore.Save(_config))
            {
                MessageBox.Show(this,
                    LanguageManager.Get("Configuration enregistrée.") ?? "Configuration enregistrée.",
                    LanguageManager.Get("OK") ?? "OK",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                AfficherOptions();
            }
            else
            {
                MessageBox.Show(this,
                    LanguageManager.Get("Erreur lors de la sauvegarde.") ?? "Erreur lors de la sauvegarde.",
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
        //  ShowServersStateForm, ToggleAutomaticTask, ToggleServiceManual.
        // ============================================================

        private void SurAjouter(object sender, RoutedEventArgs e)       { /* AddServer */ }
        private void SurModifier(object sender, RoutedEventArgs e)      { /* EditSelected */ }
        private void SurExporter(object sender, RoutedEventArgs e)      { /* ExportConfig */ }
        private void SurImporter(object sender, RoutedEventArgs e)      { /* ImportConfig */ }
        private void SurSupprimer(object sender, RoutedEventArgs e)     { /* DeleteSelected */ }
        private void SurEtatsServeurs(object sender, RoutedEventArgs e) { /* ShowServersStateForm */ }
        private void SurAutomatique(object sender, RoutedEventArgs e)   { /* ToggleAutomaticTask */ }
        private void SurService(object sender, RoutedEventArgs e)       { /* ToggleServiceManual */ }
    }
}
