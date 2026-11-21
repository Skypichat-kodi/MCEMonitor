using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MCEMonitor.Languages;

namespace MCEMonitor.UI
{
    /// <summary>
    /// Remplace les MessageBox Windows par une fenêtre au style de l'application.
    /// </summary>
    public partial class FenetreConfirmation : Window
    {
        public MessageBoxResult Resultat { get; private set; } = MessageBoxResult.No;

        public FenetreConfirmation(string message, string titre, bool confirmation, bool alerte)
        {
            InitializeComponent();

            TexteTitre.Text   = titre;
            TexteMessage.Text = message;

            // Confirmation : deux boutons. Information : un seul bouton de fermeture.
            if (confirmation)
            {
                BoutonNon.Visibility = Visibility.Visible;
                BoutonNon.Content    = LanguageManager.Get("Non") ?? "Non";
                BoutonOui.Content    = LanguageManager.Get("Oui") ?? "Oui";
            }
            else
            {
                BoutonOui.Content = "OK";
            }

            Icone.Text       = alerte ? "\uE7BA" : "\uE946";   // triangle d'alerte ou pastille d'information
            Icone.Foreground = (Brush)FindResource(alerte ? "Erreur" : "Accent");
        }

        private void EnTete_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (System.InvalidOperationException) { }
            }
        }

        private void SurOui(object sender, RoutedEventArgs e)
        {
            Resultat = BoutonNon.Visibility == Visibility.Visible
                ? MessageBoxResult.Yes
                : MessageBoxResult.OK;

            Close();
        }

        private void SurNon(object sender, RoutedEventArgs e)
        {
            Resultat = MessageBoxResult.No;
            Close();
        }

        private void SurFermer(object sender, RoutedEventArgs e)
        {
            // Fermer par la croix équivaut à refuser.
            Resultat = BoutonNon.Visibility == Visibility.Visible
                ? MessageBoxResult.No
                : MessageBoxResult.OK;

            Close();
        }
    }
}
