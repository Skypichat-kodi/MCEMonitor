using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MCEMonitor.Languages;

namespace RomMonitor.UI
{
    /// <summary>
    /// Remplace les MessageBox Windows par une fenêtre au style du module.
    /// Copie autonome : les modules ne partagent aucune ressource.
    /// </summary>
    public partial class FenetreDialogue : Window
    {
        public MessageBoxResult Resultat { get; private set; } = MessageBoxResult.No;

        public FenetreDialogue(string message, string titre, bool confirmation, bool alerte)
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
            // TryFindResource plutôt que FindResource : au démarrage, la boîte peut
            // s'ouvrir avant que le thème soit appliqué, et FindResource lèverait une
            // exception qui masquerait le message d'origine.
            Icone.Foreground = TryFindResource(alerte ? "Erreur" : "Accent") as Brush
                               ?? new SolidColorBrush(alerte
                                      ? Color.FromRgb(0xE5, 0x39, 0x35)    // rouge d'alerte
                                      : Color.FromRgb(0x4C, 0xC2, 0xFF));  // bleu d'accent
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
