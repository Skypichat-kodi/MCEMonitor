using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using MCEMonitor.Languages;
using MCEMonitor.UI.Partage;

namespace MCEMonitor.UI
{
    /// <summary>
    /// Fenêtre d'obtention du certificat. Elle s'ouvre quand la configuration
    /// l'exige, sans bouton dédié, et montre les étapes au fil de leur exécution.
    /// </summary>
    public partial class FenetreObtention : Window
    {
        private readonly CancellationTokenSource _annulation = new();
        private readonly HttpsConfig _config;

        /// <summary>Vrai si le certificat a été obtenu.</summary>
        public bool Reussi { get; private set; }

        public FenetreObtention(HttpsConfig config)
        {
            InitializeComponent();

            _config = config;
            Loaded += async (_, _) => await ExecuterAsync();
            Closed += (_, _) => _annulation.Cancel();
        }

        private async System.Threading.Tasks.Task ExecuterAsync()
        {
            var etapes = new Progress<string>(message => TexteEtape.Text = message);

            try
            {
                await GestionnaireCertificat.ObtenirAsync(_config, etapes, _annulation.Token);

                Reussi = true;
                BarreProgression.IsIndeterminate = false;
                BarreProgression.Value = 1;
                TexteEtape.Text = LanguageManager.Get("Certificat installé.") ?? "Certificat installé.";
            }
            catch (OperationCanceledException)
            {
                TexteEtape.Text = LanguageManager.Get("Obtention annulée.") ?? "Obtention annulée.";
            }
            catch (Exception ex)
            {
                TexteEtape.Text = (LanguageManager.Get("Échec :") ?? "Échec :") + " " + ex.Message;
                CoreLog.Error("HTTPS", "Obtention du certificat : " + ex.Message);
            }
            finally
            {
                BarreProgression.IsIndeterminate = false;
                BoutonAnnuler.Visibility = Visibility.Collapsed;
                BoutonFermer.Visibility = Visibility.Visible;
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
