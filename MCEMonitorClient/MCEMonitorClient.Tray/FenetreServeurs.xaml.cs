using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MCEMonitor.Languages;
using MCEMonitorClient.Tray.Ipc;
using MCEMonitorClient.Tray.Models;

namespace MCEMonitorClient.Tray
{
    /// <summary>
    /// Popup d'état des serveurs, en WPF.
    ///
    /// Reprend l'ancien ServersPopupForm : mêmes colonnes, même tri par gravité
    /// (critical > warning > ok > offline), même rafraîchissement toutes les
    /// 10 secondes, même double-clic qui ouvre la page web du serveur.
    /// </summary>
    public partial class FenetreServeurs : Window
    {
        private const int RANG_CRITIQUE = 0;
        private const int RANG_ALERTE   = 1;
        private const int RANG_OK       = 2;
        private const int RANG_HORS_LIGNE = 3;

        private readonly ObservableCollection<LigneEtat> _lignes = new();

        private readonly DispatcherTimer _minuterie =
            new() { Interval = TimeSpan.FromSeconds(10) };

        public FenetreServeurs()
        {
            InitializeComponent();

            GrilleServeurs.ItemsSource = _lignes;

            _minuterie.Tick += async (_, _) => await RafraichirAsync();
            _minuterie.Start();

            Loaded += async (_, _) => await RafraichirAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            _minuterie.Stop();
            base.OnClosed(e);
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

        private void SurFermer(object sender, RoutedEventArgs e) => Close();

        // ============================================================
        //  Rafraîchissement (ancien RefreshAsync)
        // ============================================================

        private async Task RafraichirAsync()
        {
            try
            {
                TrayState? etat = await ServiceIpcClient.GetStateAsync();

                if (etat == null)
                {
                    TexteStatut.Text = LanguageManager.Get("Service non joignable")
                        ?? "Service non joignable";
                    return;
                }

                _lignes.Clear();

                foreach (PollResult serveur in etat.Servers
                             .OrderBy(GetRangPriorite)
                             .ThenBy(s => s.ServerName))
                {
                    _lignes.Add(new LigneEtat(serveur));
                }

                int enLigne = etat.Servers.Count(s => s.Online);
                int total   = etat.Servers.Count;

                TexteStatut.Text = string.Format(
                    LanguageManager.Get("{0}/{1} serveur(s) en ligne  —  Dernière mise à jour : {2}")
                        ?? "{0}/{1} serveur(s) en ligne  —  Dernière mise à jour : {2}",
                    enLigne, total, DateTime.Now.ToString("HH:mm:ss"));
            }
            catch (Exception ex)
            {
                TexteStatut.Text = (LanguageManager.Get("Erreur : ") ?? "Erreur : ") + ex.Message;
            }
        }

        /// <summary>Forcer un scan complet, puis relire l'état (ancien ForceRefreshAsync).</summary>
        private async void SurActualiser(object sender, RoutedEventArgs e)
        {
            try
            {
                BoutonActualiser.IsEnabled = false;
                BoutonActualiser.Content   = LanguageManager.Get("Actualisation...") ?? "Actualisation...";
                TexteStatut.Text           = LanguageManager.Get("Scan en cours...") ?? "Scan en cours...";

                await ServiceIpcClient.ForceScanAsync();
                await Task.Delay(1500);
                await RafraichirAsync();
            }
            catch (Exception ex)
            {
                TexteStatut.Text = (LanguageManager.Get("Erreur : ") ?? "Erreur : ") + ex.Message;
            }
            finally
            {
                BoutonActualiser.IsEnabled = true;
                BoutonActualiser.Content   = LanguageManager.Get("Actualiser") ?? "Actualiser";
            }
        }

        // ============================================================
        //  Double-clic : ouvre la page web du serveur
        // ============================================================

        private void SurDoubleClic(object sender, MouseButtonEventArgs e)
        {
            if (GrilleServeurs.SelectedItem is not LigneEtat ligne)
                return;

            if (string.IsNullOrWhiteSpace(ligne.BaseUrl))
                return;

            try
            {
                string langue = LanguageManager.CurrentLanguage ?? "fr-FR";
                string separateur = ligne.BaseUrl.Contains('?') ? "&" : "?";
                string url = $"{ligne.BaseUrl}{separateur}lang={langue}";

                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { }
        }

        // ============================================================
        //  Tri : critical > warning > ok > offline
        // ============================================================

        private static int GetRangPriorite(PollResult serveur)
        {
            // Un serveur hors ligne reste en bas, même si son Status contient
            // encore un « critical » devenu obsolète.
            if (!serveur.Online) return RANG_HORS_LIGNE;

            string statut = (serveur.Status ?? "ok").ToLowerInvariant();

            if (statut == "critical") return RANG_CRITIQUE;
            if (statut == "warning")  return RANG_ALERTE;

            return RANG_OK;
        }
    }

    /// <summary>Ligne du popup : un serveur et sa pastille d'état.</summary>
    public class LigneEtat
    {
        private static readonly Brush PinceauOk       = Figer(0x6C, 0xCB, 0x5F);
        private static readonly Brush PinceauAlerte   = Figer(0xFF, 0xB9, 0x00);
        private static readonly Brush PinceauCritique = Figer(0xFF, 0x63, 0x47);
        private static readonly Brush PinceauInconnu  = Figer(0x88, 0x88, 0x88);

        private static Brush Figer(byte r, byte g, byte b)
        {
            var pinceau = new SolidColorBrush(Color.FromRgb(r, g, b));
            pinceau.Freeze();
            return pinceau;
        }

        public LigneEtat(PollResult serveur)
        {
            ServerName  = serveur.ServerName;
            ServiceType = serveur.ServiceType;
            BaseUrl     = serveur.BaseUrl;
            Etat        = serveur.Online
                ? (string.IsNullOrEmpty(serveur.Status) ? "ok" : serveur.Status)
                : "offline";
        }

        public string ServerName  { get; }
        public string ServiceType { get; }
        public string BaseUrl     { get; }
        public string Etat        { get; }

        public Brush CouleurEtat =>
            Etat switch
            {
                "ok"       => PinceauOk,
                "warning"  => PinceauAlerte,
                "critical" => PinceauCritique,
                "offline"  => PinceauInconnu,
                _          => PinceauInconnu
            };
    }
}
