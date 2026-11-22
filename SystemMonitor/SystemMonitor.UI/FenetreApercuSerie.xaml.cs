using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MCEMonitor.Languages;
using Point = System.Windows.Point;
using Line = System.Windows.Shapes.Line;
using Polyline = System.Windows.Shapes.Polyline;

namespace SystemMonitor.UI
{
    /// <summary>
    /// Aperçu de la courbe enregistrée avec une entrée d'historique.
    ///
    /// Le graphique principal est glissant : quand un processus entre dans
    /// l'historique, sa courbe a déjà défilé. Le service recopie donc les points
    /// dans l'entrée, et cette fenêtre les redessine — même plusieurs jours après.
    /// </summary>
    public partial class FenetreApercuSerie : Window
    {
        private readonly List<SysProcessSeriesPoint> _points;

        public FenetreApercuSerie(string nom, List<SysProcessSeriesPoint> points)
        {
            InitializeComponent();

            _points = points ?? new List<SysProcessSeriesPoint>();

            TexteTitre.Text = nom;

            if (_points.Count >= 2)
            {
                DateTime debut = _points[0].timestamp;
                DateTime fin   = _points[^1].timestamp;

                double duree = (fin - debut).TotalMinutes;
                double pic   = _points.Max(p => p.cpuUsage);
                double dernier = _points[^1].cpuUsage;

                TexteResume.Text = $"{debut:dd/MM HH:mm:ss} → {fin:HH:mm:ss}   ·   "
                                 + $"durée {duree:F0} min   ·   pic {pic:F0} %   ·   "
                                 + $"dernière valeur {dernier:F0} %";
            }
            else
            {
                TexteResume.Text = LanguageManager.Get("Aucune courbe enregistrée pour cette entrée.")
                                   ?? "Aucune courbe enregistrée pour cette entrée.";
            }

            Loaded += (_, _) => Dessiner();
        }

        private void EnTete_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { /* bouton relâché avant le déplacement */ }
            }
        }

        private void SurFermer(object sender, RoutedEventArgs e) => Close();

        private void ZoneCourbe_SizeChanged(object sender, SizeChangedEventArgs e) => Dessiner();

        /// <summary>Trace la courbe enregistrée, à la même échelle que le graphique principal.</summary>
        private void Dessiner()
        {
            double w = ZoneCourbe.ActualWidth;
            double h = ZoneCourbe.ActualHeight;

            if (w <= 0 || h <= 0)
                return;

            // Ligne médiane, comme repère de lecture.
            foreach (Line ligne in ZoneCourbe.Children.OfType<Line>())
            {
                if (ligne.Tag?.ToString() == "Grid50")
                {
                    ligne.X1 = 0;         ligne.Y1 = h * 0.5;
                    ligne.X2 = w;         ligne.Y2 = h * 0.5;
                }
            }

            Courbe.Points.Clear();

            if (_points.Count < 2)
                return;

            double pas = w / (double)(_points.Count - 1);

            for (int i = 0; i < _points.Count; i++)
            {
                double valeur = Math.Clamp(_points[i].cpuUsage, 0, 100);
                Courbe.Points.Add(new Point(i * pas, h - valeur / 100.0 * h));
            }
        }
    }
}
