using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

        /// <summary>Lignes affichées : elles portent l'état de disponibilité.</summary>
        private readonly ObservableCollection<LigneServeur> _lignes = new();

        /// <summary>Interrogation des serveurs toutes les 30 secondes, comme avant.</summary>
        private readonly DispatcherTimer _minuterieEtats = new() { Interval = TimeSpan.FromSeconds(30) };

        /// <summary>Surveillance du service et du Tray toutes les 2 secondes.</summary>
        private readonly DispatcherTimer _minuterieService = new() { Interval = TimeSpan.FromSeconds(2) };

        private bool _bandeauAffiche;

        /// <summary>Popup des états, ouvert au plus une fois.</summary>
        private MCEMonitorClient.Tray.FenetreServeurs? _fenetreServeurs;

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

            GrilleServeurs.ItemsSource = _lignes;
            RechargerLignes();

            AfficherOptions();

            // Surveillance périodique : serveurs toutes les 30 s, service toutes les 2 s.
            _minuterieEtats.Tick   += async (_, _) => await RafraichirEtatsAsync();
            _minuterieEtats.Start();

            _minuterieService.Tick += (_, _) => MettreAJourEtatService();
            _minuterieService.Start();

            // Première interrogation des serveurs dès l'ouverture, comme avant.
            _ = RafraichirEtatsAsync();

            MettreAJourEtatService();

            // Vérifie/crée la tâche du Tray au démarrage, comme l'ancien formulaire.
            EnsureTrayTaskExists();

            MettreAJourBandeau();
            Microsoft.Win32.SystemEvents.PowerModeChanged += SurChangementAlimentation;

            TexteStatut.Text = string.Format(
                LanguageManager.Get("Fichier : {0}") ?? "Fichier : {0}",
                ServerConfigStore.GetConfigPath());
        }

        protected override void OnClosed(EventArgs e)
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= SurChangementAlimentation;
            _minuterieEtats.Stop();
            _minuterieService.Stop();
            base.OnClosed(e);
        }

        /// <summary>Reconstruit les lignes de la grille à partir de la configuration.</summary>
        private void RechargerLignes()
        {
            _lignes.Clear();

            foreach (ServerEntry serveur in _config.Servers)
                _lignes.Add(new LigneServeur(serveur));
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

            // La liste des serveurs n'est pas observable : on redessine les lignes.
            foreach (LigneServeur ligne in _lignes)
                ligne.RafraichirProprietes();
        }

        /// <summary>Correspond à SaveAll de l'ancien MainForm.</summary>
        private void SurEnregistrer(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(ChampIntervalle.Text, NumberStyles.Integer,
                              CultureInfo.CurrentCulture, out int intervalle)
                || intervalle < IntervalleMin || intervalle > IntervalleMax)
            {
                Boite.Show(this,
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
                Boite.Show(this,
                    LanguageManager.Get("Configuration enregistrée.") ?? "Configuration enregistrée.",
                    LanguageManager.Get("OK") ?? "OK",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                AfficherOptions();
            }
            else
            {
                Boite.Show(this,
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

        private ServerEntry? ServeurSelectionne() => (GrilleServeurs.SelectedItem as LigneServeur)?.Source;

        private void SurAjouter(object sender, RoutedEventArgs e)
        {
            var dlg = new FenetreServeur { Owner = this };

            if (dlg.ShowDialog() == true)
            {
                _config.Servers.Add(dlg.Result);
                RechargerLignes();
            }
        }

        private void SurModifier(object sender, RoutedEventArgs e)
        {
            ServerEntry? serveur = ServeurSelectionne();

            if (serveur == null) { SignalerSelection(); return; }

            var dlg = new FenetreServeur(serveur) { Owner = this };

            if (dlg.ShowDialog() == true)
                RechargerLignes();
        }

        private void SurSupprimer(object sender, RoutedEventArgs e)
        {
            ServerEntry? serveur = ServeurSelectionne();

            if (serveur == null) { SignalerSelection(); return; }

            MessageBoxResult reponse = Boite.Show(this,
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
            RechargerLignes();
        }

        private void SignalerSelection()
        {
            Boite.Show(this,
                LanguageManager.Get("Sélectionnez un serveur.") ?? "Sélectionnez un serveur.",
                LanguageManager.Get("Info") ?? "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MessageErreurSauvegarde()
        {
            Boite.Show(this,
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

                Boite.Show(this,
                    string.Format(
                        LanguageManager.Get("Configuration exportée : {0}") ?? "Configuration exportée : {0}",
                        dlg.FileName),
                    LanguageManager.Get("OK") ?? "OK",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Boite.Show(this, (LanguageManager.Get("Erreur export :") ?? "Erreur export :") + " " + ex.Message,
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
                    Boite.Show(this,
                        LanguageManager.Get("Le fichier ne contient pas une configuration valide.")
                            ?? "Le fichier ne contient pas une configuration valide.",
                        LanguageManager.Get("Erreur") ?? "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBoxResult confirm = Boite.Show(this,
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
                    GrilleServeurs.ItemsSource = _lignes;
                    RechargerLignes();
                    AfficherOptions();

                    Boite.Show(this,
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
                Boite.Show(this, (LanguageManager.Get("Erreur import :") ?? "Erreur import :") + " " + ex.Message,
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ============================================================
        //  État des serveurs (ancien RefreshAllStatesAsync)
        // ============================================================

        private async Task RafraichirEtatsAsync()
        {
            foreach (LigneServeur ligne in _lignes.Where(l => l.Enabled).ToList())
            {
                try
                {
                    var handler = new HttpClientHandler
                    {
                        Credentials = new NetworkCredential(ligne.Source.Username, ligne.Source.Password),
                        PreAuthenticate = true
                    };

                    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                    using var resp = await http.GetAsync(ligne.Source.ApiSummaryUrl);

                    ligne.Etat = resp.IsSuccessStatusCode ? "ok" : "offline";
                }
                catch
                {
                    ligne.Etat = "offline";
                }
            }
        }

        // ============================================================
        //  Service et Tray (anciens UpdateServiceStatus, ToggleAutomaticTask,
        //  ToggleServiceManual, StartService, StopService, StartTray, StopTray)
        // ============================================================

        private static readonly Color CouleurAllume = Color.FromRgb(0x6C, 0xCB, 0x5F);
        private static readonly Color CouleurEteint = Color.FromRgb(0xFF, 0x63, 0x47);

        private static bool ServiceEnCours() =>
            Process.GetProcessesByName("MCEMonitorClient.Service").Length > 0;

        private static bool TrayEnCours() =>
            Process.GetProcessesByName("MCEMonitorClient.Tray").Length > 0;

        private void MettreAJourEtatService()
        {
            bool service = ServiceEnCours();
            bool tray    = TrayEnCours();

            bool tacheService = TaskSchedulerHelper.ClientServiceTaskExists();
            bool tacheTray    = ServiceInstaller.TrayTaskExists();

            // Bouton 1 : démarrage automatique (tâche planifiée du service)
            BoutonAutomatique.Content = tacheService
                ? (LanguageManager.Get("Automatique ON")  ?? "Automatique ON")
                : (LanguageManager.Get("Automatique OFF") ?? "Automatique OFF");

            BoutonAutomatique.Style = (Style)FindResource(tacheService ? "BoutonSucces" : "BoutonDanger");

            // Bouton 2 : service et Tray en cours
            bool tousActifs = service && tray;

            BoutonService.Content = tousActifs
                ? (LanguageManager.Get("ON")  ?? "ON")
                : (LanguageManager.Get("OFF") ?? "OFF");

            BoutonService.Style = (Style)FindResource(tousActifs ? "BoutonSucces" : "BoutonDanger");

            // Pastilles d'état
            PastilleService.Fill = new SolidColorBrush(service ? CouleurAllume : CouleurEteint);
            PastilleTray.Fill    = new SolidColorBrush(tray    ? CouleurAllume : CouleurEteint);

            // Texte explicatif
            string statut =
                tacheService && tacheTray ? "Auto (Service + Tray)" :
                tacheService             ? "Auto Service uniquement" :
                                           "Pas de démarrage auto";

            TexteEtatService.Text = LanguageManager.Get(statut) ?? statut;
            TexteEtatService.Foreground = (Brush)FindResource(tousActifs ? "Succes" : "TexteSecondaire");

            // Le popup des états a besoin du service pour répondre
            BoutonEtatsServeurs.IsEnabled = service;
        }

        /// <summary>Vérifie/crée la tâche Tray au démarrage, comme l'ancien formulaire.</summary>
        private static void EnsureTrayTaskExists()
        {
            try
            {
                if (ServiceInstaller.TrayTaskExists())
                    return;

                ServiceInstaller.CreateTrayTask();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EnsureTrayTaskExists : " + ex.Message);
            }
        }

        private void SurAutomatique(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TaskSchedulerHelper.ClientServiceTaskExists())
                {
                    if (!Confirmer("Voulez-vous vraiment désactiver le démarrage automatique ?\nLe service ne sera plus lancé au démarrage de Windows."))
                        return;

                    TaskSchedulerHelper.DeleteClientServiceTask();
                    Informer("Démarrage automatique désactivé.");
                }
                else
                {
                    if (!Confirmer("Voulez-vous activer le démarrage automatique du service ?\nLe service sera lancé au démarrage de Windows."))
                        return;

                    TaskSchedulerHelper.CreateClientServiceTask();
                    Informer("Démarrage automatique activé.");
                }

                MettreAJourEtatService();
            }
            catch (Exception ex)
            {
                Boite.Show(this, (LanguageManager.Get("Erreur") ?? "Erreur") + " : " + ex.Message,
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SurService(object sender, RoutedEventArgs e)
        {
            if (ServiceEnCours())
            {
                ArreterService();
                ArreterTray();
            }
            else
            {
                DemarrerService();
                DemarrerTray();
            }

            MettreAJourEtatService();
        }

        private bool Confirmer(string cle)
        {
            return Boite.Show(this,
                LanguageManager.Get(cle) ?? cle,
                LanguageManager.Get("Confirmation") ?? "Confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        private void Informer(string cle)
        {
            Boite.Show(this,
                LanguageManager.Get(cle) ?? cle,
                LanguageManager.Get("OK") ?? "OK",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DemarrerService()
        {
            try
            {
                string chemin = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "MCEMonitorClient.Service.exe");

                if (!File.Exists(chemin))
                {
                    Boite.Show(this,
                        string.Format(LanguageManager.Get("Impossible de trouver :\n{0}") ?? "Impossible de trouver :\n{0}", chemin),
                        LanguageManager.Get("Erreur") ?? "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Process.Start(new ProcessStartInfo { FileName = chemin, UseShellExecute = true });
                System.Threading.Thread.Sleep(1200);
            }
            catch (Exception ex)
            {
                Boite.Show(this,
                    string.Format(LanguageManager.Get("Erreur au démarrage du service : {0}") ?? "Erreur au démarrage du service : {0}", ex.Message));
            }
        }

        private void ArreterService()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("MCEMonitorClient.Service"))
                {
                    try { p.Kill(); } catch { }
                }

                System.Threading.Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                Boite.Show(this,
                    string.Format(LanguageManager.Get("Erreur à l'arrêt du service : {0}") ?? "Erreur à l'arrêt du service : {0}", ex.Message));
            }
        }

        private void DemarrerTray()
        {
            try
            {
                if (TrayEnCours())
                    return;

                string chemin = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "MCEMonitor",
                    "MCEMonitorClient.Tray.exe");

                if (!File.Exists(chemin))
                {
                    chemin = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "MCEMonitor",
                        "MCEMonitorClient.Tray.exe");
                }

                if (!File.Exists(chemin))
                {
                    Boite.Show(this,
                        string.Format(LanguageManager.Get("Impossible de trouver :\n{0}") ?? "Impossible de trouver :\n{0}", chemin),
                        LanguageManager.Get("Erreur") ?? "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Process.Start(new ProcessStartInfo { FileName = chemin, UseShellExecute = true });
                System.Threading.Thread.Sleep(800);
            }
            catch (Exception ex)
            {
                Boite.Show(this,
                    string.Format(LanguageManager.Get("Erreur au démarrage du Tray : {0}") ?? "Erreur au démarrage du Tray : {0}", ex.Message));
            }
        }

        private void ArreterTray()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("MCEMonitorClient.Tray"))
                {
                    try { p.Kill(); } catch { }
                }

                System.Threading.Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                Boite.Show(this,
                    string.Format(LanguageManager.Get("Erreur à l'arrêt du Tray : {0}") ?? "Erreur à l'arrêt du Tray : {0}", ex.Message));
            }
        }

        // ============================================================
        //  Bandeau batterie (ancien UpdatePowerBanner)
        // ============================================================

        private void MettreAJourBandeau()
        {
            bool surBatterie = SystemParameters.PowerLineStatus == PowerLineStatus.Offline;

            if (surBatterie == _bandeauAffiche)
                return;

            _bandeauAffiche = surBatterie;
            BandeauBatterie.Visibility = surBatterie ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SurChangementAlimentation(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.StatusChange)
                Dispatcher.Invoke(MettreAJourBandeau);
        }

        private void SurEtatsServeurs(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ServiceEnCours())
                {
                    Boite.Show(this,
                        LanguageManager.Get("Le Service MCEMonitorClient n'est pas en cours d'exécution.")
                            ?? "Le Service MCEMonitorClient n'est pas en cours d'exécution.",
                        LanguageManager.Get("Information") ?? "Information",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Une seule fenêtre à la fois, comme l'ancien formulaire.
                if (_fenetreServeurs == null)
                {
                    _fenetreServeurs = new MCEMonitorClient.Tray.FenetreServeurs
                    {
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };

                    _fenetreServeurs.Closed += (_, _) => _fenetreServeurs = null;
                }

                _fenetreServeurs.Show();
                _fenetreServeurs.Activate();
            }
            catch (Exception ex)
            {
                Boite.Show(this, (LanguageManager.Get("Erreur :") ?? "Erreur :") + " " + ex.Message,
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
