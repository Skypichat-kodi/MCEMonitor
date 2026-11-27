using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MCEMonitor.Languages;
using MCEMonitor.UI.Partage;

namespace MCEMonitor.UI
{
    /// <summary>
    /// Fenêtre de téléchargement de lego, avec progression.
    ///
    /// Elle s'ouvre d'elle-même quand la configuration de l'accès distant exige
    /// l'outil : pas de bouton « télécharger », pas de lien vers un navigateur.
    /// </summary>
    public partial class FenetreTelechargement : Window
    {
        private readonly CancellationTokenSource _annulation = new();

        /// <summary>Vrai si l'installation s'est déroulée jusqu'au bout.</summary>
        public bool Reussi { get; private set; }

        public FenetreTelechargement()
        {
            InitializeComponent();

            // Le travail démarre dès que la fenêtre est visible : l'utilisateur
            // n'a rien d'autre à faire que regarder, ou annuler.
            Loaded += async (_, _) => await ExecuterAsync();
            Closed += (_, _) => _annulation.Cancel();
        }

        private async Task ExecuterAsync()
        {
            var progression = new Progress<double>(fraction =>
            {
                BarreProgression.Value = fraction;

                TexteEtat.Text = fraction >= 0.85 && fraction < 1
                    ? (LanguageManager.Get("Installation…") ?? "Installation…")
                    : $"{fraction * 100:F0} %";
            });

            try
            {
                await TelechargeurLego.InstallerAsync(progression, _annulation.Token);

                Reussi = true;
                BarreProgression.Value = 1.0;
                TexteEtat.Text = LanguageManager.Get("lego installé.") ?? "lego installé.";
                BoutonAnnuler.Visibility = Visibility.Collapsed;
                BoutonFermer.Visibility = Visibility.Visible;
            }
            catch (OperationCanceledException)
            {
                TexteEtat.Text = LanguageManager.Get("Téléchargement annulé.") ?? "Téléchargement annulé.";
                BoutonAnnuler.Visibility = Visibility.Collapsed;
                BoutonFermer.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                TexteEtat.Text = (LanguageManager.Get("Échec :") ?? "Échec :") + " " + ex.Message;
                BoutonAnnuler.Visibility = Visibility.Collapsed;
                BoutonFermer.Visibility = Visibility.Visible;

                CoreLog.Error("HTTPS", "Téléchargement de lego : " + ex.Message);
            }
        }

        private void EnTete_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { }
            }
        }

        private void SurAnnuler(object sender, RoutedEventArgs e)
        {
            _annulation.Cancel();
            Close();
        }

        private void SurFermer(object sender, RoutedEventArgs e) => Close();
    }
}
