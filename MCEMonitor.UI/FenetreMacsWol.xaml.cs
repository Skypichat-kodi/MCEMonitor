using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MCEMonitor.Languages;

namespace MCEMonitor.UI
{
    /// <summary>
    /// Gestion de la liste blanche des adresses MAC réveillables,
    /// reprise de FormWolMacManager.
    /// </summary>
    public partial class FenetreMacsWol : Window
    {
        // Les MAC arrivent parfois sans séparateurs : 12 caractères collés.
        private static readonly Regex FormatMac = new(@"^[0-9A-F]{2}(:[0-9A-F]{2}){5}$");

        private static string CheminListe => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "AllowedWolMacs.txt");

        // Message courant affiché dans le champ quand la saisie est refusée.
        private string? _erreurSaisie;

        public FenetreMacsWol()
        {
            InitializeComponent();

            ChargerListe();
            MetAJourIndice();

            // La fenêtre secondaire reçoit sa propre marge d'ombre : on la centre
            // sur la fenêtre principale, comme le faisait ShowDialog(this).
            Loaded += (s, e) => Owner ??= Application.Current?.MainWindow;
        }

        private void EnTete_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { }
            }
        }

        private void ChargerListe()
        {
            ListeMacs.Items.Clear();

            if (File.Exists(CheminListe))
            {
                foreach (string ligne in File.ReadAllLines(CheminListe))
                {
                    string mac = ligne.Trim().ToUpperInvariant();

                    if (mac.Length > 0)
                        ListeMacs.Items.Add(mac);
                }
            }

            MetAJourNombre();
        }

        private void EnregistrerListe()
        {
            var propres = ListeMacs.Items
                .Cast<string>()
                .Select(m => m.Trim().ToUpperInvariant())
                .Where(m => m.Length > 0)
                .Distinct()
                .OrderBy(m => m, StringComparer.Ordinal)
                .ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(CheminListe)!);
            File.WriteAllLines(CheminListe, propres);
        }

        private void SurSaisieMac(object sender, TextChangedEventArgs e)
        {
            // Mise en forme à la volée : tirets et espaces remplacés par « : »,
            // et douze caractères collés deviennent AA:BB:CC:DD:EE:FF.
            string saisie = ChampMac.Text.Trim().ToUpperInvariant()
                .Replace('-', ':')
                .Replace(" ", string.Empty);

            if (!saisie.Contains(':') && saisie.Length == 12)
                saisie = string.Join(":", Enumerable.Range(0, 6).Select(i => saisie.Substring(i * 2, 2)));

            if (saisie == ChampMac.Text)
            {
                // La frappe efface l'erreur précédente et rend l'indication grise.
                if (_erreurSaisie != null && ChampMac.Text.Length > 0)
                {
                    _erreurSaisie = null;
                    ChampMac.ClearValue(Control.BorderBrushProperty);
                }

                MetAJourIndice();
                return;
            }

            int curseur = ChampMac.CaretIndex;

            ChampMac.TextChanged -= SurSaisieMac;
            ChampMac.Text = saisie;
            ChampMac.CaretIndex = Math.Min(curseur, ChampMac.Text.Length);
            ChampMac.TextChanged += SurSaisieMac;
        }

        private void SurAjouter(object sender, RoutedEventArgs e)
        {
            string mac = ChampMac.Text.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(mac))
            {
                SignalerErreur(LanguageManager.Get("Ui.MacRequired") ?? "Saisissez une adresse MAC");
                return;
            }

            if (!FormatMac.IsMatch(mac))
            {
                // La saisie refusée est retirée : le champ redevient vide et porte
                // le message, plutôt que de le juxtaposer à un texte fautif.
                ChampMac.Clear();
                SignalerErreur(LanguageManager.Get("Ui.MacInvalid") ?? "Format invalide");
                return;
            }

            if (ListeMacs.Items.Contains(mac))
            {
                ChampMac.Clear();
                SignalerErreur(LanguageManager.Get("Ui.MacDuplicate") ?? "Déjà dans la liste");
                return;
            }

            ListeMacs.Items.Add(mac);

            ChampMac.Clear();
            _erreurSaisie = null;
            ChampMac.ClearValue(Control.BorderBrushProperty);
            MetAJourIndice();
            MetAJourNombre();
        }

        private void SurSupprimer(object sender, RoutedEventArgs e)
        {
            if (ListeMacs.SelectedItem is string selection)
            {
                ListeMacs.Items.Remove(selection);
                MetAJourNombre();
            }
        }

        private void SurFermer(object sender, RoutedEventArgs e)
        {
            EnregistrerListe();
            Close();
        }

        private void SignalerErreur(string message)
        {
            _erreurSaisie = message;
            ChampMac.BorderBrush = (Brush)FindResource("Erreur");
            MetAJourIndice();
        }

        // Le champ vide affiche soit l'exemple grisé, soit le message d'erreur en rouge.
        private void MetAJourIndice()
        {
            if (ChampMac.Text.Length > 0)
            {
                IndiceMac.Visibility = Visibility.Collapsed;
                return;
            }

            IndiceMac.Visibility = Visibility.Visible;
            IndiceMac.Text = _erreurSaisie ?? "Exemple : AA:BB:CC:DD:EE:FF";
            IndiceMac.Foreground = (Brush)FindResource(_erreurSaisie == null ? "TexteSecondaire" : "Erreur");
        }

        private void MetAJourNombre()
        {
            int nombre = ListeMacs.Items.Count;

            TexteNombre.Text = nombre == 0
                ? LanguageManager.Get("Ui.MacNone") ?? "Aucune adresse"
                : nombre == 1 ? LanguageManager.Get("Ui.MacOne") ?? "1 adresse" : string.Format(LanguageManager.Get("Ui.MacCount") ?? "{0} adresses", nombre);
        }
    }
}
