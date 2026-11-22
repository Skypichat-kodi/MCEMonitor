using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MCEMonitor.Languages;
using MCEMonitorClient.Config.Models;

namespace MCEMonitorClient.Config
{
    /// <summary>
    /// Dialogue d'ajout / de modification d'un serveur.
    ///
    /// Reprend l'ancien ServerEditForm : mêmes champs, même test de connexion
    /// HTTP sur /api/summary, même détection automatique du type de service
    /// (déclenchée à la sortie des champs URL et Port, avec un délai de
    /// regroupement de 400 ms).
    /// </summary>
    public partial class FenetreServeur : Window
    {
        private readonly ServerEntry _entry;
        private readonly bool _isNew;

        private string _detectedServiceType = "";
        private int _autoDetectVersion;

        /// <summary>Entrée renseignée par le dialogue, comme l'ancien Result.</summary>
        public ServerEntry Result => _entry;

        public FenetreServeur(ServerEntry? existing = null)
        {
            InitializeComponent();

            _isNew = existing == null;
            _entry = existing ?? new ServerEntry();

            TexteTitre.Text = _isNew
                ? (LanguageManager.Get("Ajouter un serveur") ?? "Ajouter un serveur")
                : (LanguageManager.Get("Modifier le serveur") ?? "Modifier le serveur");

            if (_isNew)
            {
                ChampUrl.Text  = "http://";
                ChampPort.Text = _entry.Port.ToString(CultureInfo.CurrentCulture);
            }
            else
            {
                ChargerEntree();
            }
        }

        private void ChargerEntree()
        {
            ChampNom.Text   = _entry.Name;
            ChampUrl.Text   = _entry.BaseUrl;
            ChampPort.Text  = Math.Clamp(_entry.Port, 1, 65535).ToString(CultureInfo.CurrentCulture);
            ChampLogin.Text = _entry.Username;

            ChampMotDePasse.Password        = _entry.Password;
            ChampMotDePasseVisible.Text     = _entry.Password;

            SetDetectedService(_entry.ServiceType);
        }

        // ============================================================
        //  Barre de titre
        // ============================================================

        private void EnTete_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { /* bouton relâché avant le déplacement */ }
            }
        }

        private void SurAnnuler(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ============================================================
        //  Enregistrement
        // ============================================================

        private void SurEnregistrer(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ChampNom.Text))
            {
                MessageBox.Show(this,
                    LanguageManager.Get("Le nom est obligatoire.") ?? "Le nom est obligatoire.",
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                ChampNom.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(ChampUrl.Text))
            {
                MessageBox.Show(this,
                    LanguageManager.Get("L'URL est obligatoire.") ?? "L'URL est obligatoire.",
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                ChampUrl.Focus();
                return;
            }

            if (!int.TryParse(ChampPort.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int port)
                || port < 1 || port > 65535)
            {
                MessageBox.Show(this,
                    LanguageManager.Get("Le port doit être compris entre 1 et 65535.")
                        ?? "Le port doit être compris entre 1 et 65535.",
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                ChampPort.Focus();
                ChampPort.SelectAll();
                return;
            }

            // On ne rejette pas les URLs sans port explicite : FullUrl est
            // reconstruite à partir du champ Port.
            _entry.Name        = ChampNom.Text.Trim();
            _entry.BaseUrl     = ChampUrl.Text.Trim().TrimEnd('/');
            _entry.Port        = port;
            _entry.Username    = ChampLogin.Text.Trim();
            _entry.Password    = MotDePasseSaisi();
            _entry.ServiceType = GetDetectedService();

            DialogResult = true;
            Close();
        }

        /// <summary>Mot de passe en cours de saisie, selon le champ visible.</summary>
        private string MotDePasseSaisi() =>
            ChampMotDePasseVisible.Visibility == Visibility.Visible
                ? ChampMotDePasseVisible.Text
                : ChampMotDePasse.Password;

        // ============================================================
        //  Affichage du mot de passe
        // ============================================================

        private void SurAfficherMotDePasse(object sender, RoutedEventArgs e)
        {
            if (ChampMotDePasse.Visibility == Visibility.Visible)
            {
                ChampMotDePasseVisible.Text       = ChampMotDePasse.Password;
                ChampMotDePasseVisible.Visibility = Visibility.Visible;
                ChampMotDePasse.Visibility        = Visibility.Collapsed;
                ChampMotDePasseVisible.Focus();
                ChampMotDePasseVisible.CaretIndex = ChampMotDePasseVisible.Text.Length;
            }
            else
            {
                ChampMotDePasse.Password        = ChampMotDePasseVisible.Text;
                ChampMotDePasse.Visibility      = Visibility.Visible;
                ChampMotDePasseVisible.Visibility = Visibility.Collapsed;
                ChampMotDePasse.Focus();
            }
        }

        // ============================================================
        //  Détection du service
        // ============================================================

        private void SetDetectedService(string? serviceType)
        {
            _detectedServiceType = serviceType ?? "";

            TexteType.Text = string.IsNullOrWhiteSpace(serviceType)
                ? "—"
                : serviceType!;
        }

        private string GetDetectedService() =>
            string.IsNullOrWhiteSpace(_detectedServiceType) ? "Inconnu" : _detectedServiceType;

        private async void SurUrlPerdue(object sender, RoutedEventArgs e)  => await AutoDetectAsync();
        private async void SurPortPerdu(object sender, RoutedEventArgs e) => await AutoDetectAsync();

        /// <summary>
        /// Regroupe les modifications rapides en un seul test, comme l'ancien
        /// formulaire : on attend 400 ms et on abandonne si la saisie a changé.
        /// </summary>
        private async Task AutoDetectAsync()
        {
            string url = ChampUrl.Text.Trim();

            if (string.IsNullOrWhiteSpace(url) || url == "http://" || url == "https://")
                return;

            int maVersion = ++_autoDetectVersion;
            await Task.Delay(400);

            if (maVersion != _autoDetectVersion)
                return;

            await TesterConnexionAsync(autoMode: true);
        }

        private async void SurTester(object sender, RoutedEventArgs e) => await TesterConnexionAsync(autoMode: false);

        /// <summary>
        /// Appelle /api/summary et lit les champs « service » et « machine ».
        /// Reprend exactement l'ancien TestConnectionAsync.
        /// </summary>
        private async Task TesterConnexionAsync(bool autoMode)
        {
            if (!BoutonTester.IsEnabled)
                return;   // test déjà en cours

            BoutonTester.IsEnabled = false;
            TexteResultat.Foreground = (System.Windows.Media.Brush)FindResource("TexteSecondaire");
            TexteResultat.Text = autoMode
                ? (LanguageManager.Get("Détection...") ?? "Détection...")
                : (LanguageManager.Get("Test en cours...") ?? "Test en cours...");

            try
            {
                string baseUrl = ChampUrl.Text.Trim().TrimEnd('/');
                int port = int.TryParse(ChampPort.Text, out int p) ? p : _entry.Port;
                string url = $"{baseUrl}:{port}/api/summary";

                var handler = new HttpClientHandler
                {
                    Credentials = new NetworkCredential(ChampLogin.Text.Trim(), MotDePasseSaisi()),
                    PreAuthenticate = true
                };

                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                using var resp = await http.GetAsync(url);

                if (!resp.IsSuccessStatusCode)
                {
                    TexteResultat.Foreground = (System.Windows.Media.Brush)FindResource("Erreur");
                    TexteResultat.Text = $"[KO] Erreur HTTP {(int)resp.StatusCode}";
                    return;
                }

                string json = await resp.Content.ReadAsStringAsync();

                string serviceDetecte = "Inconnu";
                string machineDetectee = "";

                try
                {
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("service", out var serviceProp))
                        serviceDetecte = serviceProp.GetString() ?? "Inconnu";

                    if (doc.RootElement.TryGetProperty("machine", out var machineProp))
                        machineDetectee = machineProp.GetString() ?? "";
                }
                catch { /* JSON invalide */ }

                SetDetectedService(serviceDetecte);

                string hote = baseUrl;
                try { hote = new Uri(baseUrl).Host; } catch { }

                string machine = !string.IsNullOrEmpty(machineDetectee) ? machineDetectee : hote;

                // Le nom n'est proposé que s'il est vide ou visiblement automatique.
                bool renommer =
                    string.IsNullOrWhiteSpace(ChampNom.Text)
                    || ChampNom.Text == "http://"
                    || ChampNom.Text.StartsWith("Service ")
                    || ChampNom.Text.StartsWith("SystemMonitor (")
                    || ChampNom.Text.StartsWith("RomMonitor (")
                    || ChampNom.Text.StartsWith("MediaMonitor (")
                    || ChampNom.Text.StartsWith("NetMonitor (")
                    || ChampNom.Text.StartsWith("Inconnu (");

                if (renommer)
                    ChampNom.Text = $"Service {serviceDetecte} sur {machine}";

                TexteResultat.Foreground = (System.Windows.Media.Brush)FindResource("Succes");
                TexteResultat.Text = $"[OK] Service {serviceDetecte} sur {machine}";
            }
            catch (TaskCanceledException)
            {
                TexteResultat.Foreground = (System.Windows.Media.Brush)FindResource("Erreur");
                TexteResultat.Text = "[KO] Timeout (5s)";
            }
            catch (Exception ex)
            {
                TexteResultat.Foreground = (System.Windows.Media.Brush)FindResource("Erreur");
                TexteResultat.Text = "[KO] " + ex.Message;
            }
            finally
            {
                BoutonTester.IsEnabled = true;
            }
        }
    }
}
