using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
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

        private ServersConfig _config;

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

        // ============================================================
        //  Actions sur les serveurs (ancien AddServer / EditSelected /
        //  DeleteSelected / ExportConfig / ImportConfig)
        // ============================================================

        private ServerEntry? ServeurSelectionne() => GrilleServeurs.SelectedItem as ServerEntry;

        private void SurAjouter(object sender, RoutedEventArgs e)
        {
            var dlg = new FenetreServeur { Owner = this };

            if (dlg.ShowDialog() == true)
            {
                _config.Servers.Add(dlg.Result);
                GrilleServeurs.Items.Refresh();
            }
        }

        private void SurModifier(object sender, RoutedEventArgs e)
        {
            ServerEntry? serveur = ServeurSelectionne();

            if (serveur == null) { SignalerSelection(); return; }

            var dlg = new FenetreServeur(serveur) { Owner = this };

            if (dlg.ShowDialog() == true)
                GrilleServeurs.Items.Refresh();
        }

        private void SurSupprimer(object sender, RoutedEventArgs e)
        {
            ServerEntry? serveur = ServeurSelectionne();

            if (serveur == null) { SignalerSelection(); return; }

            MessageBoxResult reponse = MessageBox.Show(this,
                string.Format(
                    LanguageManager.Get("Voulez-vous vraiment supprimer le serveur :\n\n{0} ?")
                        ?? "Voulez-vous vraiment supprimer le serveur :\n\n{0} ?",
                    serveur.Name),
                LanguageManager.Get("Confirmation") ?? "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (reponse != MessageBoxResult.Yes)
                return;

            _config.Servers.Remove(serveur);
            GrilleServeurs.Items.Refresh();
        }

        private void SignalerSelection()
        {
            MessageBox.Show(this,
                LanguageManager.Get("Sélectionnez un serveur.") ?? "Sélectionnez un serveur.",
                LanguageManager.Get("Info") ?? "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MessageErreurSauvegarde()
        {
            MessageBox.Show(this,
                LanguageManager.Get("Erreur lors de la sauvegarde.") ?? "Erreur lors de la sauvegarde.",
                LanguageManager.Get("Erreur") ?? "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void SurExporter(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title      = LanguageManager.Get("Exporter la configuration") ?? "Exporter la configuration",
                Filter     = "Configuration MCEMonitorClient (*.config)|*.config|Tous les fichiers (*.*)|*.*",
                FileName   = $"MCEMonitorClient_{DateTime.Now:yyyy-MM-dd}.config",
                DefaultExt = "config",
                AddExtension = true,
                OverwritePrompt = true
            };

            if (dlg.ShowDialog(this) != true)
                return;

            // Le fichier courant est réécrit avant d'être copié, comme avant.
            if (!ServerConfigStore.Save(_config)) { MessageErreurSauvegarde(); return; }

            try
            {
                File.Copy(ServerConfigStore.GetConfigPath(), dlg.FileName, overwrite: true);

                MessageBox.Show(this,
                    string.Format(
                        LanguageManager.Get("Configuration exportée : {0}") ?? "Configuration exportée : {0}",
                        dlg.FileName),
                    LanguageManager.Get("OK") ?? "OK",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Erreur export : " + ex.Message,
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SurImporter(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title        = LanguageManager.Get("Importer une configuration") ?? "Importer une configuration",
                Filter       = "Configuration MCEMonitorClient (*.config)|*.config|Tous les fichiers (*.*)|*.*",
                DefaultExt   = "config",
                CheckFileExists = true,
                Multiselect  = false
            };

            if (dlg.ShowDialog(this) != true)
                return;

            try
            {
                string json = File.ReadAllText(dlg.FileName);

                ServersConfig? importe = JsonSerializer.Deserialize<ServersConfig>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (importe == null)
                {
                    MessageBox.Show(this,
                        LanguageManager.Get("Le fichier ne contient pas une configuration valide.")
                            ?? "Le fichier ne contient pas une configuration valide.",
                        LanguageManager.Get("Erreur") ?? "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBoxResult confirm = MessageBox.Show(this,
                    string.Format(
                        LanguageManager.Get("Remplacer la configuration actuelle ?")
                            ?? "Remplacer la configuration actuelle ?\n\nServeurs actuels : {0}\nServeurs dans le fichier : {1}",
                        _config.Servers.Count,
                        importe.Servers.Count),
                    LanguageManager.Get("Confirmation") ?? "Confirmation",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                    return;

                // Sauvegarde de l'ancienne configuration en .bak
                try
                {
                    File.Copy(ServerConfigStore.GetConfigPath(),
                              ServerConfigStore.GetConfigPath() + ".bak", overwrite: true);
                }
                catch { }

                if (ServerConfigStore.Save(importe))
                {
                    _config = importe;
                    GrilleServeurs.ItemsSource = _config.Servers;
                    AfficherOptions();

                    MessageBox.Show(this,
                        string.Format(
                            LanguageManager.Get("Configuration importée : {0} serveur(s).")
                                ?? "Configuration importée : {0} serveur(s).",
                            importe.Servers.Count),
                        LanguageManager.Get("OK") ?? "OK",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageErreurSauvegarde();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Erreur import : " + ex.Message,
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SurEtatsServeurs(object sender, RoutedEventArgs e) { /* ShowServersStateForm */ }
        private void SurAutomatique(object sender, RoutedEventArgs e)   { /* ToggleAutomaticTask */ }
        private void SurService(object sender, RoutedEventArgs e)       { /* ToggleServiceManual */ }
    }
}
