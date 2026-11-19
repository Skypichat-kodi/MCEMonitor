using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace NetMonitor.UI
{
    public partial class SeverityPickerWindow : Window
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

        /// <summary>
        /// "Critical" / "Warning" / "Safe" / "Trusted" / "Whitelisted" / "auto"
        /// </summary>
        public string SelectedSeverity { get; private set; } = "auto";

        public SeverityPickerWindow(string ip, string currentSeverity, bool isManual)
        {
            InitializeComponent();

            IpText.Text = $"IP : {ip}";

            // Pré-sélection :
            //  - Si l'utilisateur avait forcé une sévérité manuelle → on coche cette sévérité
            //  - Sinon → on coche "Auto"
            string target = isManual ? currentSeverity : "auto";
            SelectTag(target);
        }

        private void SelectTag(string tag)
        {
            foreach (var rb in AllRadios())
            {
                if (string.Equals(rb.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    rb.IsChecked = true;
                    return;
                }
            }
            RbAuto.IsChecked = true;
        }

        private IEnumerable<RadioButton> AllRadios()
        {
            yield return RbCritical;
            yield return RbWarning;
            yield return RbToCheck;     
            yield return RbCrawler;     
            yield return RbBot;        
            yield return RbSafe;
            yield return RbTrusted;
            yield return RbWhitelisted;
            yield return RbAuto;
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            foreach (var rb in AllRadios())
            {
                if (rb.IsChecked == true)
                {
                    SelectedSeverity = (rb.Tag as string) ?? "auto";
                    DialogResult = true;
                    return;
                }
            }
            DialogResult = false;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}