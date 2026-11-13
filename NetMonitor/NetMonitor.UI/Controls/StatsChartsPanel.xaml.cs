using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;

namespace NetMonitor.UI.Controls
{
    public partial class StatsChartsPanel : UserControl
    {
        // Palette partagée avec la version web
        private static readonly SKColor Blue    = SKColor.Parse("#4CC2FF");
        private static readonly SKColor Red     = SKColor.Parse("#FF6347");
        private static readonly SKColor Orange  = SKColor.Parse("#FFB900");
        private static readonly SKColor Green   = SKColor.Parse("#6CCB5F");
        private static readonly SKColor Purple  = SKColor.Parse("#7B4FE0");
        private static readonly SKColor Gray    = SKColor.Parse("#666666");

        private static readonly SKColor TextColor  = SKColor.Parse("#E5E5E5");
        private static readonly SKColor AxisColor  = SKColor.Parse("#3C3C3C");

        private static readonly SKColor[] Palette =
        {
            Blue, Red, Orange, Green, Purple,
            SKColor.Parse("#FF8A72"), SKColor.Parse("#41AB5D"), SKColor.Parse("#9ECAE1")
        };

        public StatsChartsPanel()
        {
            InitializeComponent();
            ApplyTheme();
        }

        // Événement pour prévenir MainWindow du changement
        public event Action<TimeGranularity>? GranularityChanged;

        private void CmbGranularityChart_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbGranularityChart.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not string tag) return;
            if (!Enum.TryParse<TimeGranularity>(tag, out var g)) return;

            GranularityChanged?.Invoke(g);
        }

        /// <summary>
        /// Applique le thème sombre aux 6 charts (une seule fois au démarrage).
        /// </summary>
        private void ApplyTheme()
        {
            foreach (var chart in new[] { ChartPorts, ChartHours, ChartDays, ChartRemotePorts })
            {
                if (chart == null) continue;
                chart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden;
            }

            foreach (var chart in new[] { ChartSeverity, ChartDetectors })
            {
                if (chart == null) continue;
                chart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Right;
            }
        }

        /// <summary>
        /// Met à jour les 6 graphiques depuis les statistiques reçues.
        /// </summary>
        public void Update(GlobalStatsDto stats)
        {
            if (stats == null) return;

            UpdatePortsChart(ChartPorts, stats.TopLocalPorts, "Ports locaux", Blue);
            UpdatePortsChart(ChartRemotePorts, stats.TopRemotePorts, "Ports distants", Orange);

            UpdateSeverityChart(ChartSeverity, stats.SeverityDistribution);
            UpdateDetectorsChart(ChartDetectors, stats.TopDetectors);

            // ⚠ Le graphique "Activité par période" (ChartHours) est géré EXCLUSIVEMENT
            // par MainWindow.ApplyGranularity() → UpdateActivitySeries(), en fonction
            // de la granularité choisie par l'utilisateur (Heure/Jour/Semaine/Mois/Année).
            // Ne PAS l'écraser ici avec Hour, sinon la sélection utilisateur est perdue
            // à chaque refresh.

            UpdateDaysChart(ChartDays, stats.ActivityByDay);          // répartition hebdo conservée
        }

        /// <summary>
        /// Met à jour le graphique "Activité par période" (ChartHours) selon
        /// la granularité choisie (Heure/Jour/Semaine/Mois/Année).
        /// </summary>
        public void UpdateActivitySeries(List<TimeBucketDto> buckets, TimeGranularity g)
        {
            if (ChartHours == null) return;

            if (buckets == null || buckets.Count == 0)
            {
                ChartHours.Series = Array.Empty<ISeries>();
                return;
            }

            var labels = buckets.Select(b => b.Label).ToArray();
            var counts = buckets.Select(b => (double)b.Count).ToArray();

            var color = g switch
            {
                TimeGranularity.Hour  => Blue,
                TimeGranularity.Day   => Orange,
                TimeGranularity.Week  => Green,
                TimeGranularity.Month => Purple,
                TimeGranularity.Year  => SKColor.Parse("#FF8A72"),
                _ => Blue
            };

            ChartHours.Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = counts,
                    Name = "Alertes",
                    Stroke = new SolidColorPaint(color, 2),
                    Fill = new SolidColorPaint(color.WithAlpha(40)),
                    GeometryFill = new SolidColorPaint(color),
                    GeometryStroke = new SolidColorPaint(SKColor.Parse("#1E1E1E"), 2),
                    GeometrySize = buckets.Count > 20 ? 4 : 8,   // points plus petits si beaucoup de buckets
                    LineSmoothness = 0.5
                }
            };

            ChartHours.XAxes = new[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsPaint = new SolidColorPaint(TextColor),
                    SeparatorsPaint = new SolidColorPaint(AxisColor),
                    TextSize = 11,
                    MinStep = 1,
                    LabelsRotation = buckets.Count > 20 ? 45 : 0   // rotation si beaucoup de labels
                }
            };

            ChartHours.YAxes = new[]
            {
                new Axis
                {
                    LabelsPaint = new SolidColorPaint(TextColor),
                    SeparatorsPaint = new SolidColorPaint(AxisColor),
                    TextSize = 11,
                    MinLimit = 0
                }
            };
        }

        // ────────────────────────────────────────────────────────────
        //  Top ports (bar horizontal)
        // ────────────────────────────────────────────────────────────
        private void UpdatePortsChart(CartesianChart chart, List<PortStatDto> data, string tooltipLabel, SKColor color)
        {
            if (chart == null) return;

            if (data == null || data.Count == 0)
            {
                chart.Series = Array.Empty<ISeries>();
                return;
            }

            var ordered = data.OrderBy(x => x.Count).ToList();  // croissant : le plus grand à la fin = en haut
            var labels = ordered.Select(x => ":" + x.Port).ToArray();
            var values = ordered.Select(x => (double)x.Count).ToArray();

            chart.Series = new ISeries[]
            {
                new RowSeries<double>
                {
                    Values = values,
                    Fill = new SolidColorPaint(color),
                    Name = tooltipLabel,
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    DataLabelsSize = 11,
                    DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.End,
                    Rx = 4,
                    Ry = 4
                }
            };

            chart.XAxes = new[]
            {
                new Axis
                {
                    LabelsPaint = new SolidColorPaint(TextColor),
                    SeparatorsPaint = new SolidColorPaint(AxisColor),
                    TextSize = 11
                }
            };

            chart.YAxes = new[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsPaint = new SolidColorPaint(TextColor),
                    SeparatorsPaint = new SolidColorPaint(AxisColor),
                    TextSize = 11,
                }
            };
        }

        // ────────────────────────────────────────────────────────────
        //  Distribution sévérité (doughnut)
        // ────────────────────────────────────────────────────────────
        private void UpdateSeverityChart(PieChart chart, List<SeverityStatDto> data)
        {
            if (chart == null) return;

            if (data == null || data.Count == 0)
            {
                chart.Series = Array.Empty<ISeries>();
                return;
            }

            var sevColors = new Dictionary<string, SKColor>(StringComparer.OrdinalIgnoreCase)
            {
                { "Whitelist",   SKColor.Parse("#FFFFFF") },   // Blanc
                { "Whitelisted", SKColor.Parse("#FFFFFF") },   // Blanc (legacy)
                { "Safe",        SKColor.Parse("#6CCB5F") },   // Vert
                { "Bot",         SKColor.Parse("#5BC0EB") },   // Bleu clair
                { "Crawler",     SKColor.Parse("#1E88E5") },   // Bleu foncé
                { "Trusted",     SKColor.Parse("#FDD835") },   // Jaune
                { "ToCheck",     SKColor.Parse("#E91E63") },   // Magenta
                { "Warning",     SKColor.Parse("#FFB900") },   // Orange
                { "Info",        SKColor.Parse("#FFB900") },   // Orange (legacy = Warning)
                { "High",        SKColor.Parse("#B71C1C") },   // Rouge foncé (legacy = Critical)
                { "Critical",    SKColor.Parse("#B71C1C") }    // Rouge foncé
            };

            var series = data
                .Where(x => x.Count > 0)
                .Select(x => new PieSeries<double>
                {
                    Values = new[] { (double)x.Count },
                    Name = x.Severity,
                    Fill = new SolidColorPaint(
                        sevColors.TryGetValue(x.Severity, out var c) ? c : Gray),
                    InnerRadius = 60,
                    Pushout = 0,                                       // ← pas de popout au survol
                    DataLabelsPaint = new SolidColorPaint(TextColor),  // ← gris clair au lieu de blanc
                    DataLabelsSize = 12,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                    DataLabelsRotation = 0,          // ← texte horizontal (par défaut : suit l'angle)
                    HoverPushout       = 0,          // ← pas d'écartement au survol
                    DataLabelsFormatter = p => $"{p.StackedValue?.Share:P0}"
                })
                .ToArray();

            chart.Series = series;

            // Animation rapide (limite le "mouvement du cercle" au chargement)
            chart.AnimationsSpeed = TimeSpan.Zero;

            // Texte de la légende en clair (par défaut noir sur fond sombre = invisible)
            chart.LegendTextPaint = new SolidColorPaint(TextColor);
            chart.LegendTextSize  = 12;
        }

        // ────────────────────────────────────────────────────────────
        //  Types d'attaque (doughnut)
        // ────────────────────────────────────────────────────────────
        private void UpdateDetectorsChart(PieChart chart, List<DetectorStatDto> data)
        {
            if (chart == null) return;

            if (data == null || data.Count == 0)
            {
                chart.Series = Array.Empty<ISeries>();
                return;
            }

            int i = 0;
            var series = data
                .Where(x => x.Count > 0)
                .Select(x => new PieSeries<double>
                {
                    Values = new[] { (double)x.Count },
                    Name = x.Detector,                                  // ← Detector, pas Severity
                    Fill = new SolidColorPaint(Palette[i++ % Palette.Length]),  // ← Palette, pas sevColors
                    InnerRadius = 60,
                    Pushout = 0,
                    DataLabelsPaint = new SolidColorPaint(TextColor),
                    DataLabelsSize = 12,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                    DataLabelsRotation = 0,          // ← texte horizontal (par défaut : suit l'angle)
                    HoverPushout       = 0,          // ← pas d'écartement au survol
                    DataLabelsFormatter = p => $"{p.StackedValue?.Share:P0}"
                })
                .ToArray();

            chart.Series = series;

            // Animation rapide (limite le "mouvement du cercle" au chargement)
            chart.AnimationsSpeed = TimeSpan.Zero;

            // Texte de la légende en clair (par défaut noir sur fond sombre = invisible)
            chart.LegendTextPaint = new SolidColorPaint(TextColor);
            chart.LegendTextSize  = 12;
        }

        // ────────────────────────────────────────────────────────────
        //  Activité par heure (courbe)
        // ────────────────────────────────────────────────────────────
        private void UpdateHoursChart(CartesianChart chart, List<HourlyStatDto> data)
        {
            if (chart == null) return;

            if (data == null || data.Count == 0)
            {
                chart.Series = Array.Empty<ISeries>();
                return;
            }

            var labels = data.Select(x => x.Hour + "h").ToArray();
            var values = data.Select(x => (double)x.Count).ToArray();

            chart.Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = values,
                    Name = "Alertes",
                    Stroke = new SolidColorPaint(Blue, 2),
                    Fill = new SolidColorPaint(Blue.WithAlpha(40)),
                    GeometryFill = new SolidColorPaint(Blue),
                    GeometryStroke = new SolidColorPaint(SKColor.Parse("#1E1E1E"), 2),
                    GeometrySize = 8,
                    LineSmoothness = 0.5
                }
            };

            chart.XAxes = new[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsPaint = new SolidColorPaint(TextColor),
                    SeparatorsPaint = new SolidColorPaint(AxisColor),
                    TextSize = 11
                }
            };

            chart.YAxes = new[]
            {
                new Axis
                {
                    LabelsPaint = new SolidColorPaint(TextColor),
                    SeparatorsPaint = new SolidColorPaint(AxisColor),
                    TextSize = 11,
                    MinLimit = 0
                }
            };
        }

        // ────────────────────────────────────────────────────────────
        //  Activité par jour (courbe)
        // ────────────────────────────────────────────────────────────
        private void UpdateDaysChart(CartesianChart chart, List<DailyStatDto> data)
        {
            if (chart == null) return;

            if (data == null || data.Count == 0)
            {
                chart.Series = Array.Empty<ISeries>();
                return;
            }

            var labels = data.Select(x => x.DayName?.Substring(0, Math.Min(3, x.DayName.Length)) ?? "?").ToArray();
            var values = data.Select(x => (double)x.Count).ToArray();

            chart.Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = values,
                    Name = "Alertes",
                    Stroke = new SolidColorPaint(Orange, 2),
                    Fill = new SolidColorPaint(Orange.WithAlpha(40)),
                    GeometryFill = new SolidColorPaint(Orange),
                    GeometryStroke = new SolidColorPaint(SKColor.Parse("#1E1E1E"), 2),
                    GeometrySize = 8,
                    LineSmoothness = 0.5
                }
            };

            chart.XAxes = new[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsPaint = new SolidColorPaint(TextColor),
                    SeparatorsPaint = new SolidColorPaint(AxisColor),
                    TextSize = 11
                }
            };

            chart.YAxes = new[]
            {
                new Axis
                {
                    LabelsPaint = new SolidColorPaint(TextColor),
                    SeparatorsPaint = new SolidColorPaint(AxisColor),
                    TextSize = 11,
                    MinLimit = 0
                }
            };
        }
    }
}