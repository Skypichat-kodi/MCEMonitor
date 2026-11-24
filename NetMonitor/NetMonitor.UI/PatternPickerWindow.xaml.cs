using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace NetMonitor.UI
{
    public partial class PatternPickerWindow : Window
    {
        // ============================================================
        //  Barre de titre personnalisée (WindowStyle=None)
        //  Ce dialogue n'a pas d'en-tête natif : le XAML en ajoute un (titre lié
        //  à Window.Title + bouton Fermer). Pas de Réduire/Agrandir : la fenêtre
        //  est en ResizeMode=NoResize.
        // ============================================================

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr fenetre, int attribut, ref int valeur, int taille);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;   // Windows 11
        private const int DWMWCP_ROUND                   = 2;

        /// <summary>
        /// Coins arrondis via DWM (WindowChrome.CornerRadius est sans effet sous
        /// Windows 11). Sous Windows 10 l'attribut est inconnu, l'appel est sans effet.
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                int arrondi = DWMWCP_ROUND;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref arrondi, sizeof(int));
            }
            catch
            {
                // Windows 10 : rien à faire, la fenêtre reste à angles droits.
            }
        }

        /// <summary>Déplacement depuis l'en-tête (la fenêtre n'a plus de barre native).</summary>
        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { /* bouton relâché avant le déplacement */ }
            }
        }

        private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();

        // ============================================================

        private readonly BlockedIpRow _row;

        /// <summary>"safe" / "critical" / "scanner" / "crawler" / "whitelist"</summary>
        public string SelectedList { get; private set; } = "safe";

        /// <summary>La valeur du pattern à ajouter (ISP, Org, ReverseDns ou IP)</summary>
        public string SelectedValue { get; private set; } = "";

        public PatternPickerWindow(BlockedIpRow row)
        {
            InitializeComponent();
            _row = row;

            IpText.Text = $"IP : {row.Ip}";

            // Sélectionne automatiquement la 1ère valeur disponible
            if (!string.IsNullOrWhiteSpace(row.Isp))
                RbIsp.IsChecked = true;
            else if (!string.IsNullOrWhiteSpace(row.Org))
                RbOrg.IsChecked = true;
            else if (!string.IsNullOrWhiteSpace(row.ReverseDns))
                RbReverseDns.IsChecked = true;
            else
                RbIp.IsChecked = true;

            // Sélectionne "safe" par défaut
            RbListSafe.IsChecked = true;
        }

        private void ValueRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb) return;
            if (txtSelectedValue == null) return;

            string type = rb.Tag?.ToString() ?? "isp";
            txtSelectedValue.Text = type switch
            {
                "isp"        => _row.Isp        ?? "-",
                "org"        => _row.Org        ?? "-",
                "reversedns" => _row.ReverseDns ?? "-",
                "ip"         => _row.Ip         ?? "-",
                _            => "-"
            };
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            // Vérifie qu'une valeur est bien sélectionnée
            if (string.IsNullOrWhiteSpace(txtSelectedValue.Text) || txtSelectedValue.Text == "-")
            {
                Boite.Show(this, 
                    "La valeur sélectionnée est vide ou indisponible pour cette IP.",
                    "NetMonitor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Récupère la liste cible
            foreach (var rb in new[] { RbListSafe, RbListCritical, RbListScanner, RbListCrawler, RbListWhitelist })
            {
                if (rb.IsChecked == true)
                {
                    SelectedList = (rb.Tag as string) ?? "safe";
                    break;
                }
            }

            SelectedValue = txtSelectedValue.Text.Trim();
            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}