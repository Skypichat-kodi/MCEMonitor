using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Polyline = System.Windows.Shapes.Polyline;
using Line = System.Windows.Shapes.Line;
using Rectangle = System.Windows.Shapes.Rectangle;
using MCEMonitor.Languages;
using System.Linq;

namespace SystemMonitor.UI
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<GpuCard> _gpus = new();
        private readonly ObservableCollection<NetworkCard> _networks = new();
        private readonly ObservableCollection<AlertRow> _alerts = new();
        private List<SysHistoryPoint> _history = new();
        private readonly ObservableCollection<BsodRow> _bsods = new();        
        
        private readonly DispatcherTimer _refreshTimer;
        private bool _isRefreshing = false;
        private bool _loadingWebConfig = false;

        private SysSnapshot? _lastSnapshot;

        public MainWindow()
        {
            InitializeComponent();

            // Vérification : lancement uniquement depuis MCEMonitor
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 2 || args[1] != "--from-mcem")
            {
                MessageBox.Show(
                    "SystemMonitor.UI ne peut être lancé que depuis MCEMonitor.",
                    "Accès refusé",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            // Vérification : service en cours
            bool serviceRunning = Process.GetProcessesByName("SystemMonitor.Service").Length > 0;

            if (!serviceRunning)
            {
                MessageBox.Show(
                    "Le service SystemMonitor n'est pas démarré.\n" +
                    "Veuillez lancer MCEMonitor pour le démarrer automatiquement.",
                    "Service non démarré",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Close();
                return;
            }

            GpusList.ItemsSource = _gpus;
            NetworksList.ItemsSource = _networks;
            AlertsGrid.ItemsSource = _alerts;
            BsodsGrid.ItemsSource = _bsods;            

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += async (_, __) => await RefreshSafe();
            _refreshTimer.Start();

            _ = RefreshSafe();
            LoadWatchConfig();
            _ = LoadWebConfig();
        }

        // ------------------------------------------------------------
        //  Refresh
        // ------------------------------------------------------------
        private async Task RefreshSafe()
        {
            if (_isRefreshing) return;

            _isRefreshing = true;
            try
            {
                var snap = await SystemMonitorIpcClient.GetSnapshot();

                if (snap == null)
                {
                    StatusText.Text = "Service SystemMonitor non joignable";
                    return;
                }

                _lastSnapshot = snap;

                UpdateCpu(snap.cpu);
                UpdateRam(snap.ram);
                UpdateGpus(snap.gpus);
                UpdateNetworks(snap.networks);
                _ = RefreshAlerts();
                _ = RefreshHistory();
                _ = RefreshBsods();                                

            StatusText.Text =
                $"{LanguageManager.Get("Dernière mise à jour") ?? "Dernière mise à jour"} : {snap.timestamp:HH:mm:ss}  |  " +
                $"CPU : {snap.cpu.usagePercent:F1}%  |  " +
                $"RAM : {snap.ram.usagePercent:F1}%  |  " +
                $"{snap.gpus.Count} GPU  |  " +
                $"{snap.networks.Count} {LanguageManager.Get("réseau(x)") ?? "réseau(x)"}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Erreur IPC : " + ex.Message;
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        // ------------------------------------------------------------
        //  CPU
        // ------------------------------------------------------------
        private void UpdateCpu(SysCpu cpu)
        {
            txtCpuName.Text = cpu.name;

            double usage = cpu.usagePercent;
            txtCpuUsage.Text = $"{usage:F1} %";
            barCpuUsage.Value = usage;
            barCpuUsage.Foreground = new SolidColorBrush(GetColorForPercent(usage));

            txtCpuTemp.Text = cpu.temperature.HasValue
                ? $"{cpu.temperature.Value:F1} °C"
                : "N/A";

            txtCpuFreq.Text = cpu.frequencyMHz.HasValue
                ? $"{cpu.frequencyMHz.Value:F0} MHz"
                : "N/A";

            txtCpuMaxFreq.Text = cpu.maxFrequencyMHz.HasValue
                ? $"{cpu.maxFrequencyMHz.Value:F0} MHz"
                : "N/A";

            // Cœurs
            coresList.Items.Clear();

            foreach (var core in cpu.cores)
            {
                var brush = new SolidColorBrush(GetColorForPercent(core.usagePercent));

                var panel = new StackPanel
                {
                    Width = 32,
                    Margin = new Thickness(2, 0, 2, 6),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                // Label "C01"
                var lblName = new TextBlock
                {
                    Text = core.name.Replace("Core ", "C"),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                panel.Children.Add(lblName);

                // Conteneur de la barre verticale
                var barBg = new Border
                {
                    Height = 45,
                    Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 2, 0, 2)
                };

                var grid = new Grid();

                // Rectangle de remplissage, ancré en bas
                var fill = new Rectangle
                {
                    Fill = brush,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    RadiusX = 3,
                    RadiusY = 3,
                    Height = Math.Max(1, core.usagePercent / 100.0 * 45)
                };

                grid.Children.Add(fill);
                barBg.Child = grid;
                panel.Children.Add(barBg);

                // Pourcentage
                var lblValue = new TextBlock
                {
                    Text = $"{core.usagePercent:F0}%",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                panel.Children.Add(lblValue);

                coresList.Items.Add(panel);
            }
        }

        // ------------------------------------------------------------
        //  RAM
        // ------------------------------------------------------------
        private void UpdateRam(SysRam ram)
        {
            txtRamUsage.Text = $"{ram.usagePercent:F1} %";
            barRamUsage.Value = ram.usagePercent;
            barRamUsage.Foreground = new SolidColorBrush(GetColorForPercent(ram.usagePercent));

            txtRamUsed.Text = $"{ram.usedGB:F1} Go";
            txtRamFree.Text = $"{ram.freeGB:F1} Go";
            txtRamTotal.Text = $"{ram.totalGB:F1} Go";
        }

        // ------------------------------------------------------------
        //  GPU
        // ------------------------------------------------------------
        private void UpdateGpus(List<SysGpu> gpus)
        {
            _gpus.Clear();

            foreach (var g in gpus)
            {
                double vramPercent = 0;
                if (g.vramUsedMB.HasValue && g.vramTotalMB.HasValue && g.vramTotalMB.Value > 0)
                    vramPercent = (g.vramUsedMB.Value / g.vramTotalMB.Value) * 100;

                var card = new GpuCard
                {
                    Name = g.name,
                    UsagePercent = g.usagePercent,
                    UsageText = $"{g.usagePercent:F1} %",
                    UsageBrush = new SolidColorBrush(GetColorForPercent(g.usagePercent)),

                    TempText = g.temperature.HasValue ? $"{g.temperature.Value:F1} °C" : "N/A",
                    TempBrush = new SolidColorBrush(GetColorForPercent(
                        g.temperature.HasValue ? Math.Min(100, (g.temperature.Value / 100.0) * 100) : 0)),

                    VramText = (g.vramUsedMB.HasValue && g.vramTotalMB.HasValue)
                        ? $"{g.vramUsedMB.Value:F0} / {g.vramTotalMB.Value:F0} Mo"
                        : "N/A",
                    VramPercent = vramPercent,
                    VramBrush = new SolidColorBrush(GetColorForPercent(vramPercent))
                };

                _gpus.Add(card);
            }
        }

        // ------------------------------------------------------------
        //  Réseau
        // ------------------------------------------------------------
        private void UpdateNetworks(List<SysNetwork> networks)
        {
            _networks.Clear();

            foreach (var n in networks)
            {
                _networks.Add(new NetworkCard
                {
                    Name = n.name,
                    DownloadText = $"{n.downloadKBps:F1} KB/s",
                    UploadText = $"{n.uploadKBps:F1} KB/s"
                });
            }
        }

        private async Task RefreshAlerts()
        {
            try
            {
                var alerts = await SystemMonitorIpcClient.GetAlerts();
                if (alerts == null) return;

                _alerts.Clear();

                foreach (var a in alerts.OrderByDescending(x => x.timestamp).Take(100))
                {
                    _alerts.Add(new AlertRow
                    {
                        Timestamp = a.timestamp,
                        Type = a.type ?? "",
                        Severity = a.severity ?? "",
                        Target = a.target ?? "",
                        Message = a.message ?? "",
                        EmailSent = a.emailSent
                    });
                }
            }
            catch { }
        }
        
        // ------------------------------------------------------------
        //  Couleur selon pourcentage (même logique que le web)
        // ------------------------------------------------------------
        private static Color GetColorForPercent(double percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            byte r, g, b;

            if (percent <= 60)
            {
                r = 108; g = 203; b = 95;
            }
            else if (percent <= 85)
            {
                double t = (percent - 60) / 25.0;
                r = (byte)(108 + (255 - 108) * t);
                g = (byte)(203 + (185 - 203) * t);
                b = (byte)(95  + (0   - 95)  * t);
            }
            else
            {
                double t = (percent - 85) / 15.0;
                r = (byte)(255 + (255 - 255) * t);
                g = (byte)(185 + (99  - 185) * t);
                b = (byte)(0   + (71  - 0)   * t);
            }

            return Color.FromRgb(r, g, b);
        }

        // ------------------------------------------------------------
        //  Boutons
        // ------------------------------------------------------------
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Rafraîchissement forcé...";

            await SystemMonitorIpcClient.ForceScanAsync();
            await Task.Delay(500);
            await RefreshSafe();
        }

        private void OpenLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "Logs"
                );

                if (!Directory.Exists(logFolder))
                    Directory.CreateDirectory(logFolder);

                Process.Start("explorer.exe", logFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir le dossier Logs.\n" + ex.Message);
            }
        }

        // ------------------------------------------------------------
        //  Serveur Web
        // ------------------------------------------------------------
        private async Task LoadWebConfig()
        {
            try
            {
                _loadingWebConfig = true;

                // ============================================================
                //  Boucle de retry : le service peut mettre quelques secondes
                //  à démarrer son serveur IPC
                // ============================================================
                SysWebStatus? status = null;

                for (int attempt = 0; attempt < 10; attempt++)
                {
                    status = await SystemMonitorIpcClient.GetWebStatus();

                    if (status != null)
                        break;

                    await Task.Delay(500);
                }

                if (status == null)
                {
                    // Après 5 secondes, on abandonne
                    return;
                }

                ToggleWeb.IsChecked = status.enabled;
                txtWebPort.Text = status.port.ToString();
                txtWebLogin.Text = status.username;

                // Mot de passe : lu depuis le fichier config local
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "SystemMonitor.config"
                );

                if (File.Exists(configPath))
                {
                    foreach (var line in File.ReadAllLines(configPath))
                    {
                        if (line.StartsWith("WebPassword=", StringComparison.OrdinalIgnoreCase))
                        {
                            string pass = line.Split('=', 2)[1].Trim();
                            txtWebPassword.Password = pass;
                            txtWebPasswordVisible.Text = pass;
                            break;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                _loadingWebConfig = false;
            }
        }

        private async void ToggleWeb_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingWebConfig) return;
            await ApplyWebConfig();
        }

        private async void ToggleWeb_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_loadingWebConfig) return;
            await ApplyWebConfig();
        }

        private async void btnApplyWeb_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await ApplyWebConfig();

            if (ok)
            {
                MessageBox.Show(
                    LanguageManager.Get("Paramètres Web mis à jour.") ?? "Paramètres Web mis à jour.",
                    "OK",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private async Task<bool> ApplyWebConfig()
        {
            try
            {
                if (!int.TryParse(txtWebPort.Text, out int port))
                {
                    MessageBox.Show("Port invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                string login = txtWebLogin.Text.Trim();

                string pass = txtWebPassword.Visibility == Visibility.Visible
                    ? txtWebPassword.Password
                    : txtWebPasswordVisible.Text;

                bool enabled = ToggleWeb.IsChecked == true;

                return await SystemMonitorIpcClient.SetWebConfig(enabled, port, login, pass);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message);
                return false;
            }
        }

        private void btnShowWebPassword_Click(object sender, RoutedEventArgs e)
        {
            if (txtWebPassword.Visibility == Visibility.Visible)
            {
                txtWebPasswordVisible.Text = txtWebPassword.Password;
                txtWebPassword.Visibility = Visibility.Collapsed;
                txtWebPasswordVisible.Visibility = Visibility.Visible;
                imgWebEye.Text = "🙈";
            }
            else
            {
                txtWebPassword.Password = txtWebPasswordVisible.Text;
                txtWebPasswordVisible.Visibility = Visibility.Collapsed;
                txtWebPassword.Visibility = Visibility.Visible;
                imgWebEye.Text = "👁";
            }
        }

        private void btnOpenWeb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(txtWebPort.Text, out int port))
                    return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = $"http://localhost:{port}/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir le navigateur : " + ex.Message);
            }
        }
        
        // ------------------------------------------------------------
        //  Alertes
        // ------------------------------------------------------------
        private void OpenHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string historyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "SystemMonitor",
                    "alert_history.json"
                );

                if (!File.Exists(historyPath))
                {
                    MessageBox.Show(
                        "Aucun historique d'alertes disponible pour l'instant.",
                        "Historique",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = historyPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir l'historique.\n" + ex.Message);
            }
        }

        private async void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "Voulez-vous vraiment vider tout l'historique des alertes ?\n" +
                    "Cette action est irréversible.",
                    "Vider l'historique",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                bool ok = await SystemMonitorIpcClient.ClearAlertsAsync();

                if (ok)
                {
                    _alerts.Clear();
                    StatusText.Text = "Historique des alertes vidé.";
                }
                else
                {
                    MessageBox.Show(
                        "Impossible de vider l'historique (service non joignable ?).",
                        "Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message);
            }
        }
        
        // ------------------------------------------------------------
        //  Réglages de surveillance
        // ------------------------------------------------------------
        private void LoadWatchConfig()
        {
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "SystemMonitor.config"
                );

                // Valeurs par défaut
                txtInterval.Text = "5";
                txtCooldown.Text = "15";
                chkAlertCpu.IsChecked = true;
                txtCpuThreshold.Text = "90";
                chkAlertRam.IsChecked = true;
                txtRamThreshold.Text = "90";
                chkAlertTemp.IsChecked = true;
                txtTempThreshold.Text = "85";

                if (!File.Exists(configPath))
                    return;

                foreach (var line in File.ReadAllLines(configPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    string val = parts[1].Trim();

                    switch (key)
                    {
                        case "Interval":             txtInterval.Text = val; break;
                        case "CpuCooldownMinutes":   txtCooldown.Text = val; break;
                        case "AlertOnHighCpu":       chkAlertCpu.IsChecked = val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case "CpuThresholdPercent":  txtCpuThreshold.Text = val; break;
                        case "AlertOnHighRam":       chkAlertRam.IsChecked = val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case "RamThresholdPercent":  txtRamThreshold.Text = val; break;
                        case "AlertOnHighTemp":      chkAlertTemp.IsChecked = val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case "TempThresholdCelsius": txtTempThreshold.Text = val; break;
                    }
                }
            }
            catch { }
        }

        private void btnSaveWatchConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "SystemMonitor.config"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

                var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Interval"] = txtInterval.Text.Trim(),
                    ["CpuCooldownMinutes"] = txtCooldown.Text.Trim(),
                    ["AlertOnHighCpu"] = (chkAlertCpu.IsChecked == true).ToString().ToLower(),
                    ["CpuThresholdPercent"] = txtCpuThreshold.Text.Trim(),
                    ["AlertOnHighRam"] = (chkAlertRam.IsChecked == true).ToString().ToLower(),
                    ["RamThresholdPercent"] = txtRamThreshold.Text.Trim(),
                    ["AlertOnHighTemp"] = (chkAlertTemp.IsChecked == true).ToString().ToLower(),
                    ["TempThresholdCelsius"] = txtTempThreshold.Text.Trim(),
                };

                var newLines = new List<string>();
                var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (File.Exists(configPath))
                {
                    foreach (var line in File.ReadAllLines(configPath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        {
                            newLines.Add(line);
                            continue;
                        }

                        var parts = line.Split('=', 2);
                        if (parts.Length != 2)
                        {
                            newLines.Add(line);
                            continue;
                        }

                        string key = parts[0].Trim();

                        if (updates.TryGetValue(key, out var newVal))
                        {
                            newLines.Add($"{key}={newVal}");
                            processed.Add(key);
                        }
                        else
                        {
                            newLines.Add(line);
                        }
                    }
                }

                foreach (var kv in updates)
                {
                    if (!processed.Contains(kv.Key))
                        newLines.Add($"{kv.Key}={kv.Value}");
                }

                File.WriteAllLines(configPath, newLines);

                MessageBox.Show(
                    "Réglages de surveillance enregistrés.\n\n" +
                    "Pour que le service les prenne en compte, redémarrez-le.",
                    "OK",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message);
            }
        }
        
        // ------------------------------------------------------------
        //  Historique
        // ------------------------------------------------------------
        private async Task RefreshHistory()
        {
            try
            {
                var history = await SystemMonitorIpcClient.GetHistory();
                if (history == null) return;

                _history = history;
                DrawCharts();
            }
            catch { }
        }

        private void Chart_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawCharts();
        }

        private void DrawCharts()
        {
            if (_history == null || _history.Count == 0)
            {
                polyCpu.Points.Clear();
                polyRam.Points.Clear();
                txtCpuHistoMax.Text = "Max : 0 %";
                txtRamHistoMax.Text = "Max : 0 %";
                return;
            }

            DrawChart(chartCpu, polyCpu, _history.Select(h => h.cpuUsage).ToList());
            DrawChart(chartRam, polyRam, _history.Select(h => h.ramUsage).ToList());

            double maxCpu = _history.Max(h => h.cpuUsage);
            double maxRam = _history.Max(h => h.ramUsage);

            txtCpuHistoMax.Text = $"Max : {maxCpu:F1} %";
            txtRamHistoMax.Text = $"Max : {maxRam:F1} %";
        }

        private void DrawChart(Canvas canvas, Polyline poly, List<double> values)
        {
            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;

            if (w <= 0 || h <= 0 || values.Count < 2)
            {
                poly.Points.Clear();
                return;
            }

            // Mettre à jour les lignes de grille
            foreach (var child in canvas.Children.OfType<Line>())
            {
                if (child.Tag?.ToString() == "Grid25")
                {
                    double y = h * 0.25;
                    child.X1 = 0; child.Y1 = y;
                    child.X2 = w; child.Y2 = y;
                }
                else if (child.Tag?.ToString() == "Grid50")
                {
                    double y = h * 0.50;
                    child.X1 = 0; child.Y1 = y;
                    child.X2 = w; child.Y2 = y;
                }
                else if (child.Tag?.ToString() == "Grid75")
                {
                    double y = h * 0.75;
                    child.X1 = 0; child.Y1 = y;
                    child.X2 = w; child.Y2 = y;
                }
            }

            // Dessiner la courbe
            poly.Points.Clear();

            double stepX = w / Math.Max(1, values.Count - 1);

            for (int i = 0; i < values.Count; i++)
            {
                double v = Math.Max(0, Math.Min(100, values[i]));
                double x = i * stepX;
                double y = h - (v / 100.0 * h);
                poly.Points.Add(new Point(x, y));
            }
        }

        private async void ClearMeasureHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "Voulez-vous vraiment vider l'historique du graphique ?\n" +
                    "Les courbes seront remises à zéro.",
                    "Vider le graphique",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                bool ok = await SystemMonitorIpcClient.ClearMeasureHistoryAsync();

                if (ok)
                {
                    _history.Clear();
                    DrawCharts();
                    StatusText.Text = "Historique du graphique vidé.";
                }
                else
                {
                    MessageBox.Show(
                        "Impossible de vider l'historique (service non joignable ?).",
                        "Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message);
            }
        }
        
        // ------------------------------------------------------------
        //  BSOD
        // ------------------------------------------------------------
        private async Task RefreshBsods()
        {
            try
            {
                var bsods = await SystemMonitorIpcClient.GetBsods();
                if (bsods == null) return;

                _bsods.Clear();

                foreach (var b in bsods.OrderByDescending(x => x.timestamp).Take(100))
                {
                    _bsods.Add(new BsodRow
                    {
                        Timestamp = b.timestamp,
                        BugCheckCode = b.bugCheckCode ?? "",
                        BugCheckName = b.bugCheckName ?? "",
                        Parameters = b.parameters ?? "",
                        DumpPath = b.dumpPath ?? "",
                        DumpExists = b.dumpExists,
                        FaultyModule = b.faultyModule ?? "",
                        FaultAddress = b.faultAddress ?? ""
                    });
                }
            }
            catch { }
        }

        private async void ClearBsods_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "Voulez-vous vraiment vider l'historique des BSOD ?\n" +
                    "Cette action est irréversible.",
                    "Vider l'historique BSOD",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                bool ok = await SystemMonitorIpcClient.ClearBsodsAsync();

                if (ok)
                {
                    _bsods.Clear();
                    StatusText.Text = "Historique BSOD vidé.";
                }
                else
                {
                    MessageBox.Show(
                        "Impossible de vider l'historique (service non joignable ?).",
                        "Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message);
            }
        }

        private void OpenMinidumpFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folder = @"C:\Windows\Minidump";

                if (!Directory.Exists(folder))
                {
                    MessageBox.Show(
                        "Le dossier C:\\Windows\\Minidump n'existe pas sur cette machine.",
                        "Info",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                Process.Start("explorer.exe", folder);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir le dossier.\n" + ex.Message);
            }
        }                                
    }

    // ============================================================
    //  ViewModels pour les DataGrid
    // ============================================================

    public class GpuCard
    {
        public string Name { get; set; } = "";
        public double UsagePercent { get; set; }
        public string UsageText { get; set; } = "";
        public Brush UsageBrush { get; set; } = Brushes.Gray;
        public string TempText { get; set; } = "";
        public Brush TempBrush { get; set; } = Brushes.Gray;
        public string VramText { get; set; } = "";
        public double VramPercent { get; set; }
        public Brush VramBrush { get; set; } = Brushes.Gray;
    }

    public class NetworkCard
    {
        public string Name { get; set; } = "";
        public string DownloadText { get; set; } = "";
        public string UploadText { get; set; } = "";
    }
    
    public class AlertRow
    {
        public DateTime Timestamp { get; set; }
        public string TimestampText => Timestamp.ToString("dd/MM/yyyy HH:mm:ss");
        public string Type { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Target { get; set; } = "";
        public string Message { get; set; } = "";
        public bool EmailSent { get; set; }
    }
    
    public class BsodRow
    {
        public DateTime Timestamp { get; set; }
        public string TimestampText => Timestamp.ToString("dd/MM/yyyy HH:mm:ss");
        public string BugCheckCode { get; set; } = "";
        public string BugCheckName { get; set; } = "";
        public string Parameters { get; set; } = "";
        public string DumpPath { get; set; } = "";
        public bool DumpExists { get; set; }
        public string FaultyModule { get; set; } = "";
        public string FaultAddress { get; set; } = "";
    }     
}