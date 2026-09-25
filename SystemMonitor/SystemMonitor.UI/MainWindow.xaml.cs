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
using Rectangle = System.Windows.Shapes.Rectangle;
using MCEMonitor.Languages;

namespace SystemMonitor.UI
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<GpuRow> _gpus = new();
        private readonly ObservableCollection<NetworkRow> _networks = new();

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

            GpusGrid.ItemsSource = _gpus;
            NetworksGrid.ItemsSource = _networks;

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += async (_, __) => await RefreshSafe();
            _refreshTimer.Start();

            _ = RefreshSafe();
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

                StatusText.Text =
                    $"Dernière mise à jour : {snap.timestamp:HH:mm:ss}  |  " +
                    $"CPU : {snap.cpu.usagePercent:F1}%  |  " +
                    $"RAM : {snap.ram.usagePercent:F1}%  |  " +
                    $"{snap.gpus.Count} GPU  |  {snap.networks.Count} réseau(x)";
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
                _gpus.Add(new GpuRow
                {
                    Name = g.name,
                    UsageText = $"{g.usagePercent:F1} %",
                    TempText = g.temperature.HasValue ? $"{g.temperature.Value:F1} °C" : "N/A",
                    VramText = (g.vramUsedMB.HasValue && g.vramTotalMB.HasValue)
                        ? $"{g.vramUsedMB.Value:F0} / {g.vramTotalMB.Value:F0} Mo"
                        : "N/A"
                });
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
                _networks.Add(new NetworkRow
                {
                    Name = n.name,
                    DownloadText = $"{n.downloadKBps:F1} KB/s",
                    UploadText = $"{n.uploadKBps:F1} KB/s"
                });
            }
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
    }

    // ============================================================
    //  ViewModels pour les DataGrid
    // ============================================================

    public class GpuRow
    {
        public string Name { get; set; } = "";
        public string UsageText { get; set; } = "";
        public string TempText { get; set; } = "";
        public string VramText { get; set; } = "";
    }

    public class NetworkRow
    {
        public string Name { get; set; } = "";
        public string DownloadText { get; set; } = "";
        public string UploadText { get; set; } = "";
    }
}