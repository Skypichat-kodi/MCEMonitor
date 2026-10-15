using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MCEMonitor.Languages;

namespace NetMonitor.UI
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<AlertRow> _alerts = new();
        private readonly ObservableCollection<ConnectionRow> _connections = new();
        private readonly DispatcherTimer _refreshTimer;
        private bool _isRefreshing = false;
        private DateTime _lastConfigWriteTime = DateTime.MinValue;
        private long _lastConfigSize = -1;
        private bool _loadingWebConfig = false;

        public MainWindow()
        {
            InitializeComponent();

            // Lancement uniquement depuis MCEMonitor
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 2 || args[1] != "--from-mcem")
            {
                MessageBox.Show(
                    "NetMonitor.UI ne peut être lancé que depuis MCEMonitor.",
                    "Accès refusé",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            // Service en cours ?
            bool serviceRunning = Process.GetProcessesByName("NetMonitor.Service").Length > 0;

            if (!serviceRunning)
            {
                MessageBox.Show(
                    "Le service NetMonitor n'est pas démarré.\n" +
                    "Veuillez lancer MCEMonitor pour le démarrer automatiquement.",
                    "Service non démarré",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Close();
                return;
            }

            AlertsGrid.ItemsSource = _alerts;
            ConnectionsGrid.ItemsSource = _connections;

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += async (_, __) => await RefreshSafe();
            _refreshTimer.Start();

            _ = RefreshSafe();
            LoadSettings();
            _ = LoadWebConfig();
        }

        // ------------------------------------------------------------
        //  Refresh principal
        // ------------------------------------------------------------
        private async Task RefreshSafe()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                CheckConfigFileChanged();

                // Statut
                var status = await NetMonitorIpcClient.GetStatus();

                if (status == null)
                {
                    StatusText.Text = "Service NetMonitor non joignable";
                    StatusDot.Fill = new SolidColorBrush(Colors.OrangeRed);
                }
                else
                {
                    StatusDot.Fill = status.running
                        ? new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F))
                        : new SolidColorBrush(Colors.Orange);

                    StatusText.Text = status.running
                        ? $"Surveillance active  |  {status.alertCount} alerte(s)  |  {status.connectionCount} connexion(s)"
                        : "Surveillance arrêtée";
                }

                // Alertes
                var alerts = await NetMonitorIpcClient.GetAlerts();
                if (alerts != null)
                {
                    _alerts.Clear();
                    foreach (var a in alerts.OrderByDescending(x => x.timestamp).Take(500))
                    {
                        _alerts.Add(new AlertRow
                        {
                            Timestamp = a.timestamp.ToLocalTime(),
                            Severity = a.severity ?? "",
                            Source = a.source ?? "",
                            Message = a.message ?? "",
                            Details = a.details,
                            RemoteIp = a.remoteIp
                        });
                    }
                }

                // Connexions
                var conns = await NetMonitorIpcClient.GetConnections();
                if (conns != null)
                {
                    _connections.Clear();
                    foreach (var c in conns.Take(300))
                    {
                        _connections.Add(new ConnectionRow
                        {
                            Timestamp = c.timestamp.ToLocalTime(),
                            Protocol = c.protocol ?? "Tcp",
                            LocalEndpoint = $"{c.localIp}:{c.localPort}",
                            RemoteEndpoint = $"{c.remoteIp}:{c.remotePort}",
                            Pid = c.pid,
                            ProcessName = c.processName ?? ""
                        });
                    }

                    txtConnectionCount.Text = $"{_connections.Count} connexion(s) récente(s)";
                }

                txtLastUpdate.Text = $"Mise à jour : {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Erreur : " + ex.Message;
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        // ------------------------------------------------------------
        //  Boutons
        // ------------------------------------------------------------
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshSafe();
        }

        private void BlockSelected_Click(object sender, RoutedEventArgs e)
        {
            if (AlertsGrid.SelectedItem is not AlertRow row)
            {
                MessageBox.Show(
                    "Sélectionnez d'abord une ligne dans la liste des intrusions.",
                    "NetMonitor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string? ip = row.RemoteIp ?? ExtractIpFromMessage(row.Message);

            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show(
                    "Impossible d'extraire une adresse IP depuis cette ligne.",
                    "NetMonitor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show(
                $"Blocage automatique d'IP :\n\nL'IP distante identifiée est {ip}.\n\n" +
                "Pour bloquer une IP manuellement, utilisez le bouton « Bloquer l'IP » qui sera branché " +
                "sur la commande IPC `net.blockip` dans une prochaine itération.",
                "NetMonitor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ClearAlerts_Click(object sender, RoutedEventArgs e)
        {
            _alerts.Clear();
            StatusText.Text = "Liste des intrusions vidée (côté affichage).";
        }

        // ------------------------------------------------------------
        //  Extraction IP
        // ------------------------------------------------------------
        private static string? ExtractIpFromMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;

            try
            {
                var parts = message.Split(' ');
                foreach (var p in parts)
                {
                    var clean = p.Trim(' ', ',', ';', ':', '(', ')', '[', ']');
                    if (System.Net.IPAddress.TryParse(clean, out _))
                        return clean;
                }
            }
            catch { }

            return null;
        }

        // ------------------------------------------------------------
        //  Réglages
        // ------------------------------------------------------------
        private async void LoadSettings()
        {
            try
            {
                var cfg = await NetMonitorIpcClient.GetConfig();
                if (cfg == null)
                {
                    // Valeurs par défaut si le service ne répond pas
                    txtPortScanWindow.Text = "10";
                    txtPortScanThreshold.Text = "20";
                    txtBeaconWindow.Text = "300";
                    txtBeaconHits.Text = "6";
                    txtBeaconJitter.Text = "20";
                    chkAutoBlock.IsChecked = false;
                    return;
                }

                txtPortScanWindow.Text = cfg.portScanWindowSeconds.ToString();
                txtPortScanThreshold.Text = cfg.portScanThreshold.ToString();
                txtBeaconWindow.Text = cfg.beaconWindowSeconds.ToString();
                txtBeaconHits.Text = cfg.beaconMinHits.ToString();
                txtBeaconJitter.Text = Math.Round(cfg.beaconJitterTolerancePct * 100).ToString();
                chkAutoBlock.IsChecked = cfg.autoBlock;
            }
            catch { }
        }

        private async void btnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "NetMonitor.config"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

                double jitterPct = 20;
                if (double.TryParse(txtBeaconJitter.Text, out double j)) jitterPct = j;

                var lines = new[]
                {
                    "# ============================================================",
                    "# NetMonitor.config",
                    "# Configuration du service NetMonitor",
                    "# ============================================================",
                    "",
                    "# --- Détecteur : Scan de ports ---",
                    $"PortScanWindowSeconds={txtPortScanWindow.Text.Trim()}",
                    $"PortScanThreshold={txtPortScanThreshold.Text.Trim()}",
                    "",
                    "# --- Détecteur : Beaconing ---",
                    $"BeaconWindowSeconds={txtBeaconWindow.Text.Trim()}",
                    $"BeaconMinHits={txtBeaconHits.Text.Trim()}",
                    $"BeaconJitterTolerancePct={(jitterPct / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    "",
                    "# --- Blocage automatique ---",
                    $"AutoBlock={(chkAutoBlock.IsChecked == true).ToString().ToLower()}"
                };

                File.WriteAllLines(configPath, lines);

                var fi = new FileInfo(configPath);
                _lastConfigWriteTime = fi.LastWriteTime;
                _lastConfigSize = fi.Length;

                bool reloaded = await NetMonitorIpcClient.ReloadConfigAsync();

                MessageBox.Show(
                    reloaded
                        ? "Réglages NetMonitor enregistrés.\n\nLe service a rechargé la configuration à chaud."
                        : "Réglages enregistrés, mais le service n'a pas pu être notifié.\nRedémarrez-le pour appliquer.",
                    "NetMonitor",
                    MessageBoxButton.OK,
                    reloaded ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message);
            }
        }

        private void CheckConfigFileChanged()
        {
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "NetMonitor.config");

                if (!File.Exists(configPath))
                    return;

                var fi = new FileInfo(configPath);

                if (fi.LastWriteTime == _lastConfigWriteTime && fi.Length == _lastConfigSize)
                    return;

                _lastConfigWriteTime = fi.LastWriteTime;
                _lastConfigSize = fi.Length;

                LoadSettings();
            }
            catch { }
        }
        
        // ------------------------------------------------------------
        //  Serveur Web
        // ------------------------------------------------------------
        private async Task LoadWebConfig()
        {
            try
            {
                _loadingWebConfig = true;

                var status = await NetMonitorIpcClient.GetWebStatus();
                if (status == null) return;

                ToggleWeb.IsChecked = status.enabled;
                txtWebPort.Text = status.port.ToString();
                txtWebLogin.Text = status.username;
                txtWebPassword.Password = status.password ?? "";
                txtWebPasswordVisible.Text = status.password ?? "";
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
                    "Paramètres Web mis à jour.",
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

                return await NetMonitorIpcClient.SetWebConfig(enabled, port, login, pass);
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
                if (!int.TryParse(txtWebPort.Text, out int port)) return;

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

    public class AlertRow
    {
        public DateTime Timestamp { get; set; }
        public string TimestampText => Timestamp.ToString("dd/MM/yyyy HH:mm:ss");
        public string Severity { get; set; } = "";
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Details { get; set; }
        public string? RemoteIp { get; set; }
    }

    public class ConnectionRow
    {
        public DateTime Timestamp { get; set; }
        public string TimestampText => Timestamp.ToString("HH:mm:ss");
        public string Protocol { get; set; } = "";
        public string LocalEndpoint { get; set; } = "";
        public string RemoteEndpoint { get; set; } = "";
        public int Pid { get; set; }
        public string ProcessName { get; set; } = "";
    }
}