using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace NetMonitor.UI
{
    public partial class OriginPickerWindow : Window
    {
        public string SelectedOrigin { get; private set; } = "auto";

        public OriginPickerWindow(string ip, string currentOrigin, bool isManual)
        {
            InitializeComponent();
            IpText.Text = $"IP : {ip}";
            SelectTag(isManual ? currentOrigin : "auto");
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
            yield return RbUnknown;
            yield return RbScanner;
            yield return RbCrawler;
            yield return RbBot;
            yield return RbMicrosoft;
            yield return RbBlacklist;
            yield return RbWhitelist;
            yield return RbAuto;
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            foreach (var rb in AllRadios())
            {
                if (rb.IsChecked == true)
                {
                    SelectedOrigin = (rb.Tag as string) ?? "auto";
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