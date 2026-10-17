using System;
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
        private readonly ObservableCollection<AlertRow> _activeIntrusions = new();
        private readonly ObservableCollection<BlockedIpRow> _blockedIps = new();
        private readonly ObservableCollection<AlertRow> _alertsHistory = new();
        private readonly ObservableCollection<ConnectionRow> _connections = new();

        private readonly DispatcherTimer _refreshTimer;
        private bool _isRefreshing = false;
        private DateTime _lastConfigWriteTime = DateTime.MinValue;
        private long _lastConfigSize = -1;
        private bool _loadingWebConfig = false;

        public MainWindow()
        {
            InitializeComponent();

            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 2 || args[1] != "--from-mcem")
            {
                MessageBox.Show(
                    "NetMonitor.UI ne peut être lancé que depuis MCEMonitor.",
                    "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            bool serviceRunning = Process.GetProcessesByName("NetMonitor.Service").Length > 0;
            if (!serviceRunning)
            {
                MessageBox.Show(
                    "Le service NetMonitor n'est pas démarré.\n" +
                    "Veuillez lancer MCEMonitor pour le démarrer automatiquement.",
                    "Service non démarré", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            ActiveIntrusionsGrid.ItemsSource = _activeIntrusions;
            BlockedIpsGrid.ItemsSource = _blockedIps;
            AlertsHistoryGrid.ItemsSource = _alertsHistory;
            ConnectionsGrid.ItemsSource = _connections;

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
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
                        ? $"Surveillance active  |  {_activeIntrusions.Count} active(s)  |  {_blockedIps.Count} bloquée(s)"
                        : "Surveillance arrêtée";
                }

                // ---- Tableau 1 : intrusions actives ----
                var active = await NetMonitorIpcClient.GetActiveIntrusions();
                if (active != null)
                {
                    string? key = GetSelectedAlertKey(ActiveIntrusionsGrid);
                    _activeIntrusions.Clear();
                    foreach (var a in active.OrderByDescending(x => x.timestamp))
                    {
                        _activeIntrusions.Add(new AlertRow
                        {
                            Timestamp = a.timestamp.ToLocalTime(),
                            Severity = a.severity ?? "",
                            Source = a.source ?? "",
                            Message = a.message ?? "",
                            Details = a.details,
                            RemoteIp = a.remoteIp
                        });
                    }
                    RestoreAlertSelection(ActiveIntrusionsGrid, _activeIntrusions, key);
                }

                // ---- Tableau 2 : IP bloquées ----
                var blocked = await NetMonitorIpcClient.GetBlockedIps();
                if (blocked != null)
                {
                    string? key = GetSelectedBlockedKey();
                    _blockedIps.Clear();
                    foreach (var b in blocked.OrderByDescending(x => x.BlockedAt))
                    {
                        _blockedIps.Add(new BlockedIpRow
                        {
                            Ip = b.Ip,
                            BlockedAt = b.BlockedAt.ToLocalTime(),
                            Source = b.Source,
                            Message = b.Message,
                            Details = b.Details
                        });
                    }
                    RestoreBlockedSelection(key);
                }

                // ---- Tableau 3 : historique ----
                var history = await NetMonitorIpcClient.GetAlerts();
                if (history != null)
                {
                    string? key = GetSelectedAlertKey(AlertsHistoryGrid);
                    _alertsHistory.Clear();
                    foreach (var a in history.OrderByDescending(x => x.timestamp).Take(500))
                    {
                        _alertsHistory.Add(new AlertRow
                        {
                            Timestamp = a.timestamp.ToLocalTime(),
                            Severity = a.severity ?? "",
                            Source = a.source ?? "",
                            Message = a.message ?? "",
                            Details = a.details,
                            RemoteIp = a.remoteIp
                        });
                    }
                    RestoreAlertSelection(AlertsHistoryGrid, _alertsHistory, key);
                }

                // ---- Connexions ----
                var conns = await NetMonitorIpcClient.GetConnections();
                if (conns != null)
                {
                    string? key = GetSelectedConnectionKey();
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
                    RestoreConnectionSelection(key);
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
        //  Restauration des sélections (évite la perte au refresh)
        // ------------------------------------------------------------
        private static string? GetSelectedAlertKey(DataGrid grid)
        {
            if (grid.SelectedItem is not AlertRow row) return null;
            return $"{row.Timestamp:O}|{row.Source}|{row.RemoteIp}|{row.Message}";
        }

        private static void RestoreAlertSelection(DataGrid grid,
            ObservableCollection<AlertRow> source, string? key)
        {
            if (string.IsNullOrEmpty(key)) return;

            foreach (var item in source)
            {
                if ($"{item.Timestamp:O}|{item.Source}|{item.RemoteIp}|{item.Message}" == key)
                {
                    grid.SelectedItem = item;
                    grid.ScrollIntoView(item);
                    return;
                }
            }
        }

        private string? GetSelectedBlockedKey()
        {
            if (BlockedIpsGrid.SelectedItem is not BlockedIpRow row) return null;
            return $"{row.Ip}|{row.BlockedAt:O}";
        }

        private void RestoreBlockedSelection(string? key)
        {
            if (string.IsNullOrEmpty(key)) return;

            foreach (var item in _blockedIps)
            {
                if ($"{item.Ip}|{item.BlockedAt:O}" == key)
                {
                    BlockedIpsGrid.SelectedItem = item;
                    BlockedIpsGrid.ScrollIntoView(item);
                    return;
                }
            }
        }

        private string? GetSelectedConnectionKey()
        {
            if (ConnectionsGrid.SelectedItem is not ConnectionRow row) return null;
            return $"{row.Timestamp:O}|{row.LocalEndpoint}|{row.RemoteEndpoint}|{row.Pid}";
        }

        private void RestoreConnectionSelection(string? key)
        {
            if (string.IsNullOrEmpty(key)) return;

            foreach (var item in _connections)
            {
                if ($"{item.Timestamp:O}|{item.LocalEndpoint}|{item.RemoteEndpoint}|{item.Pid}" == key)
                {
                    ConnectionsGrid.SelectedItem = item;
                    ConnectionsGrid.ScrollIntoView(item);
                    return;
                }
            }
        }

        // ------------------------------------------------------------
        //  Boutons : tableau 1 (intrusions actives)
        // ------------------------------------------------------------
        private async void Refresh_Click(object sender, RoutedEventArgs e)
            => await RefreshSafe();

        private async void BlockSelected_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveIntrusionsGrid.SelectedItem is not AlertRow row)
            {
                MessageBox.Show(
                    "Sélectionnez d'abord une intrusion dans le tableau du haut.",
                    "NetMonitor", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string? ip = row.RemoteIp ?? ExtractIpFromMessage(row.Message);

            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show(
                    "Impossible d'extraire une adresse IP depuis cette ligne.",
                    "NetMonitor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Bloquer définitivement l'adresse IP {ip} via le pare-feu Windows ?\n\n" +
                "Deux règles seront créées (entrée + sortie).\n" +
                "L'IP passera dans le tableau « IP bloquées ».",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            // Utilisation de la nouvelle méthode avec résultat détaillé (garde-fou)
            var (ok, errorMsg) = await NetMonitorIpcClient.BlockIpWithResultAsync(
                ip, row.Source, row.Message, row.Details);

            if (ok)
            {
                MessageBox.Show(
                    $"IP {ip} bloquée par le pare-feu.",
                    "NetMonitor", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"Échec du blocage de {ip}.\n\n{errorMsg}",
                    "NetMonitor", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            await RefreshSafe();
        }

        private async void ClearActive_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Vider la liste des intrusions détectées ?\n\n" +
                "Les IP bloquées (tableau 2) et l'historique (tableau 3) sont conservés.",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            await NetMonitorIpcClient.ClearActiveIntrusionsAsync();
            await RefreshSafe();
        }

        // ------------------------------------------------------------
        //  Boutons : tableau 2 (IP bloquées)
        // ------------------------------------------------------------
        private async void UnblockSelected_Click(object sender, RoutedEventArgs e)
        {
            string? ip = null;

            if (BlockedIpsGrid.SelectedItem is BlockedIpRow row)
                ip = row.Ip;

            if (string.IsNullOrWhiteSpace(ip))
            {
                var dlg = new InputBoxWindow("Débloquer une IP",
                    "Entrez l'adresse IP à débloquer :");
                if (dlg.ShowDialog() == true)
                    ip = dlg.Value;
            }

            if (string.IsNullOrWhiteSpace(ip)) return;

            var confirm = MessageBox.Show(
                $"Retirer les règles de blocage pour l'adresse IP {ip} ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            bool ok = await NetMonitorIpcClient.UnblockIpAsync(ip);

            MessageBox.Show(
                ok ? $"IP {ip} débloquée." : $"Échec du déblocage de {ip}.",
                "NetMonitor", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Error);

            await RefreshSafe();
        }

        // ------------------------------------------------------------
        //  Bouton : tableau 3 (historique)
        // ------------------------------------------------------------
        private async void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Effacer TOUT l'historique des intrusions ?\n\n" +
                "Cette action est irréversible.",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            await NetMonitorIpcClient.ClearAlertsAsync();
            await RefreshSafe();
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
                    txtPortScanWindow.Text = "10";
                    txtPortScanThreshold.Text = "20";
                    txtBeaconWindow.Text = "300";
                    txtBeaconHits.Text = "6";
                    txtBeaconJitter.Text = "5"; // Nouveau défaut à 5%
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
                    "MCEMonitor", "NetMonitor.config");

                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

                double jitterPct = 5;
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
                    "NetMonitor", MessageBoxButton.OK,
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
                    "MCEMonitor", "NetMonitor.config");

                if (!File.Exists(configPath)) return;

                var fi = new FileInfo(configPath);
                if (fi.LastWriteTime == _lastConfigWriteTime && fi.Length == _lastConfigSize) return;

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
            finally { _loadingWebConfig = false; }
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
                MessageBox.Show("Paramètres Web mis à jour.", "OK",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async Task<bool> ApplyWebConfig()
        {
            try
            {
                if (!int.TryParse(txtWebPort.Text, out int port))
                {
                    MessageBox.Show("Port invalide.", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
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

    public class BlockedIpRow
    {
        public string Ip { get; set; } = "";
        public DateTime BlockedAt { get; set; }
        public string BlockedAtText => BlockedAt.ToString("dd/MM/yyyy HH:mm:ss");
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Details { get; set; }
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