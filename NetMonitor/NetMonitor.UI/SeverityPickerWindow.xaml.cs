using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace NetMonitor.UI
{
    public partial class SeverityPickerWindow : Window
    {
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