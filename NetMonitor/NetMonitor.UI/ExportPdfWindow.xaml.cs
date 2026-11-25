using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MCEMonitor.Languages;

namespace NetMonitor.UI
{
    /// <summary>
    /// Choix des tableaux à inclure dans le rapport PDF.
    ///
    /// Le rapport complet mêlait cinq listes sans limite : illisible, et parfois
    /// très long. Ici, l'utilisateur coche ce qu'il veut exporter.
    /// </summary>
    public partial class ExportPdfWindow : Window
    {
        /// <summary>Sections cochées, dans l'ordre du service. Vide si aucune.</summary>
        public List<string> Sections { get; } = new();

        public ExportPdfWindow()
        {
            InitializeComponent();
        }

        private void EnTete_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { }
            }
        }

        private void SurExporter(object sender, RoutedEventArgs e)
        {
            Sections.Clear();

            if (CaseBloquees.IsChecked == true)      Sections.Add("Blocked");
            if (CaseInterceptions.IsChecked == true) Sections.Add("Retries");
            if (CaseHistorique.IsChecked == true)    Sections.Add("History");
            if (CaseAudit.IsChecked == true)         Sections.Add("Audit");
            if (CaseListeBlanche.IsChecked == true)  Sections.Add("Whitelist");

            if (Sections.Count == 0)
            {
                Boite.Show(this,
                    LanguageManager.Get("Cochez au moins un tableau à exporter.")
                    ?? "Cochez au moins un tableau à exporter.",
                    LanguageManager.Get("Exporter un rapport PDF") ?? "Exporter un rapport PDF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void SurAnnuler(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
