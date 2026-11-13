using System;
using System.Windows;
using System.Windows.Controls;

namespace NetMonitor.UI
{
    public partial class PatternPickerWindow : Window
    {
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
                MessageBox.Show(
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