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
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace NetMonitor.UI
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<GroupedAlertRow> _activeIntrusions = new();
        private readonly ObservableCollection<BlockedIpRow> _blockedIps = new();
        private readonly ObservableCollection<AlertRow> _alertsHistory = new();
        private readonly ObservableCollection<ConnectionRow> _connections = new();
        private readonly ObservableCollection<AlertRow> _auditAlerts = new();
        // --- Stats ---
        private readonly ObservableCollection<StatsIpRow>        _statsActiveIps        = new();
        private readonly ObservableCollection<StatsCountryRow>   _statsActiveCountries  = new();
        private readonly ObservableCollection<StatsDetectorRow>  _statsActiveDetectors  = new();
        private readonly ObservableCollection<StatsIpRow>        _statsBlockedIps       = new();
        private readonly ObservableCollection<StatsCountryRow>   _statsBlockedCountries = new();
        private readonly ObservableCollection<StatsDetectorRow>  _statsBlockedDetectors = new();

        private readonly DispatcherTimer _refreshTimer;
        private bool _isRefreshing = false;
        private DateTime _lastConfigWriteTime = DateTime.MinValue;
        private long _lastConfigSize = -1;
        private bool _loadingWebConfig = false;
        private bool _mapInitialized = false;
        private bool _chartsInitialized = false;

        // ⚠ NOUVEAU : hash pour éviter la reconstruction des intrusions actives
        private string? _lastActiveHash;
        // ⚠ NOUVEAU : offset du scroll global (ScrollViewer de l'onglet actif)
        private double _lastGlobalScroll = 0;

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

            ActiveIntrusionsGrid.ItemsSource      = _activeIntrusions;
            BlockedIpsGrid.ItemsSource            = _blockedIps;
            AlertsHistoryGrid.ItemsSource         = _alertsHistory;
            ConnectionsGrid.ItemsSource           = _connections;
            AuditGrid.ItemsSource                 = _auditAlerts;
            StatsActiveIpsGrid.ItemsSource        = _statsActiveIps;
            StatsActiveCountriesGrid.ItemsSource  = _statsActiveCountries;
            StatsActiveDetectorsGrid.ItemsSource  = _statsActiveDetectors;
            StatsBlockedIpsGrid.ItemsSource       = _statsBlockedIps;
            StatsBlockedCountriesGrid.ItemsSource = _statsBlockedCountries;
            StatsBlockedDetectorsGrid.ItemsSource = _statsBlockedDetectors;

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refreshTimer.Tick += async (_, __) => await RefreshSafe();
            _refreshTimer.Start();

            _ = RefreshSafe();
            LoadSettings();
            _ = LoadWebConfig();
            _ = InitMapWebViewAsync();
            _ = InitChartsWebViewAsync();
            _ = RefreshStatsAsync();
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

                // ⚠ NOUVEAU : sauvegarde du scroll global (ScrollViewer de l'onglet actif)
                _lastGlobalScroll = GetActiveTabScrollOffset();

                // Sauvegarde des offsets de scroll des DataGrid
                double activeOffset  = GetScrollOffset(ActiveIntrusionsGrid);
                double blockedOffset = GetScrollOffset(BlockedIpsGrid);
                double historyOffset = GetScrollOffset(AlertsHistoryGrid);
                double auditOffset   = GetScrollOffset(AuditGrid);
                double connsOffset   = GetScrollOffset(ConnectionsGrid);

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

                // ---- Tableau 1 : intrusions actives (groupées par IP) ----
                var active = await NetMonitorIpcClient.GetActiveIntrusions();
                if (active != null)
                {
                    // Ne reconstruire que si le contenu a changé (évite le flicker)
                    var newHash = string.Join("|", active.Select(a => $"{a.remoteIp}:{a.timestamp:O}"));
                    if (newHash != _lastActiveHash)
                    {
                        _lastActiveHash = newHash;

                        string? selectedIp = (ActiveIntrusionsGrid.SelectedItem as GroupedAlertRow)?.RemoteIp;

                        _activeIntrusions.Clear();

                        // Groupe par IP
                        var grouped = active
                            .Where(a => !string.IsNullOrWhiteSpace(a.remoteIp))
                            .GroupBy(a => a.remoteIp!)
                            .Select(g =>
                            {
                                var mostSevere = g.OrderByDescending(x => SeverityRank(x.severity)).First();
                                var mostRecent = g.OrderByDescending(x => x.timestamp).First();

                                return new GroupedAlertRow
                                {
                                    RemoteIp  = g.Key,
                                    FirstSeen = g.Min(x => x.timestamp),
                                    LastSeen  = g.Max(x => x.timestamp),
                                    Severity  = mostSevere.severity ?? "Info",
                                    Detectors = string.Join(", ", g.Select(x => x.source).Distinct().OrderBy(x => x)),
                                    Message   = mostRecent.message ?? "",
                                    Count     = g.Count(),
                                    // ⚠ NOUVEAU
                                    RemotePortsDisplay = string.Join(", ",
                                        g.Select(x => x.remotePort)
                                         .Where(p => p > 0)
                                         .Distinct()
                                         .OrderBy(p => p)
                                         .Take(5))
                                        is { Length: > 0 } s ? s : "-",
                                    Details = mostRecent.details
                                };
                            })
                            .OrderByDescending(x => SeverityRank(x.Severity))
                            .ThenByDescending(x => x.Count)
                            .ThenByDescending(x => x.LastSeen)
                            .ToList();

                        foreach (var row in grouped) _activeIntrusions.Add(row);

                        // Événements sans IP (isolés en bas)
                        foreach (var a in active.Where(x => string.IsNullOrWhiteSpace(x.remoteIp))
                                                .OrderByDescending(x => x.timestamp).Take(20))
                        {
                            _activeIntrusions.Add(new GroupedAlertRow
                            {
                                RemoteIp  = null,
                                FirstSeen = a.timestamp,
                                LastSeen  = a.timestamp,
                                Severity  = a.severity ?? "Info",
                                Detectors = a.source ?? "",
                                Message   = a.message ?? "",
                                Count     = 1,
                                RemotePortsDisplay = a.remotePort > 0 ? a.remotePort.ToString() : "-",
                                Details = a.details
                            });
                        }

                        // Restauration de la sélection (PAS de ScrollIntoView → le scroll reste où il est)
                        if (!string.IsNullOrEmpty(selectedIp))
                        {
                            foreach (var item in _activeIntrusions)
                            {
                                if (item.RemoteIp == selectedIp)
                                {
                                    ActiveIntrusionsGrid.SelectedItem = item;
                                    break;
                                }
                            }
                        }
                    }
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
                            Details = b.Details,
                            Severity = b.Severity
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
                            Timestamp  = a.timestamp.ToLocalTime(),
                            Severity   = a.severity ?? "",
                            Source     = a.source ?? "",
                            Message    = a.message ?? "",
                            Details    = a.details,
                            RemoteIp   = a.remoteIp,
                            RemotePort = a.remotePort,
                            LocalPort  = a.localPort,
                            Pid        = a.pid,
                            ProcessName = a.processName
                        });
                    }
                    RestoreAlertSelection(AlertsHistoryGrid, _alertsHistory, key);
                }

                // ---- Tableau 4 : audit (whitelist) ----
                var audit = await NetMonitorIpcClient.GetAuditHistory();
                if (audit != null)
                {
                    string? key = GetSelectedAlertKey(AuditGrid);
                    _auditAlerts.Clear();
                    foreach (var a in audit.OrderByDescending(x => x.timestamp).Take(500))
                    {
                        _auditAlerts.Add(new AlertRow
                        {
                            Timestamp  = a.timestamp.ToLocalTime(),
                            Severity   = a.severity ?? "",
                            Source     = a.source ?? "",
                            Message    = a.message ?? "",
                            Details    = a.details,
                            RemoteIp   = a.remoteIp,
                            RemotePort = a.remotePort,
                            LocalPort  = a.localPort,
                            Pid        = a.pid,
                            ProcessName = a.processName
                        });
                    }
                    RestoreAlertSelection(AuditGrid, _auditAlerts, key);
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

                // Restauration des offsets après le re-remplissage
                RestoreScrollAsync(ActiveIntrusionsGrid, activeOffset);
                RestoreScrollAsync(BlockedIpsGrid, blockedOffset);
                RestoreScrollAsync(AlertsHistoryGrid, historyOffset);
                RestoreScrollAsync(AuditGrid, auditOffset);
                RestoreScrollAsync(ConnectionsGrid, connsOffset);

                // Restauration du scroll global
                RestoreActiveTabScroll(_lastGlobalScroll);
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

        private static int SeverityRank(string? severity) => severity switch
        {
            "Critical" => 3,
            "High"     => 2,
            "Warning"  => 1,
            _          => 0
        };

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
                    return;   // ⚠ pas de ScrollIntoView → le scroll reste où il est
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
            if (ActiveIntrusionsGrid.SelectedItem is not GroupedAlertRow row)
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

            // Prend le premier détecteur ("PortScan" au lieu de "PortScan, Blacklist")
            string primaryDetector = row.Detectors?
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .FirstOrDefault() ?? "-";

            var (ok, errorMsg) = await NetMonitorIpcClient.BlockIpWithResultAsync(
                ip, primaryDetector, row.Message, null, row.Severity);

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
        //  Bouton : tableau 4 (audit)
        // ------------------------------------------------------------
        private async void ClearAudit_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Vider l'historique des alertes ignorées (IP de confiance) ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            await NetMonitorIpcClient.ClearAuditAsync();
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
                    txtBeaconJitter.Text = "5";
                    chkAutoBlock.IsChecked = false;
                    txtIgnoredNetworks.Text = "127.0.0.0/8,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16";
                    chkDisableFilters.IsChecked = false;
                    txtBlacklistLevel.Text = "3";
                    lblBlacklistLastUpdate.Text = "Dernière MAJ : inconnue";
                    return;
                }

                txtPortScanWindow.Text = cfg.portScanWindowSeconds.ToString();
                txtPortScanThreshold.Text = cfg.portScanThreshold.ToString();
                txtBeaconWindow.Text = cfg.beaconWindowSeconds.ToString();
                txtBeaconHits.Text = cfg.beaconMinHits.ToString();
                txtBeaconJitter.Text = Math.Round(cfg.beaconJitterTolerancePct * 100).ToString();
                chkAutoBlock.IsChecked = cfg.autoBlock;

                if (cfg.ignoredNetworks != null && cfg.ignoredNetworks.Length > 0)
                    txtIgnoredNetworks.Text = string.Join(",", cfg.ignoredNetworks);

                chkDisableFilters.IsChecked = cfg.disableFilters;
                txtBlacklistLevel.Text = cfg.blacklistLevel.ToString();

                if (cfg.blacklistLastUpdate != DateTime.MinValue)
                    lblBlacklistLastUpdate.Text = $"Dernière MAJ : {cfg.blacklistLastUpdate.ToLocalTime():dd/MM/yyyy HH:mm}";
                else
                    lblBlacklistLastUpdate.Text = "Dernière MAJ : inconnue";
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

                // Préserve IpLookupUrl existant (pas configurable depuis l'UI pour l'instant)
                string currentIpLookupUrl = LoadIpLookupUrl();

                double jitterPct = 5;
                if (double.TryParse(txtBeaconJitter.Text, out double j)) jitterPct = j;

                if (chkDisableFilters.IsChecked == true)
                {
                    var confirm = MessageBox.Show(
                        "Vous êtes sur le point d'activer le mode Debug.\n\n" +
                        "Cela va DÉSACTIVER toutes les whitelists. Le système risque d'être inondé de fausses alertes.\n\n" +
                        "Voulez-vous vraiment continuer ?",
                        "Confirmation mode Debug",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (confirm != MessageBoxResult.Yes) return;
                }

                string webEnabled = (ToggleWeb.IsChecked == true).ToString().ToLower();
                string webPort = txtWebPort.Text.Trim();
                string webLogin = txtWebLogin.Text.Trim();
                string webPass = txtWebPassword.Visibility == Visibility.Visible
                    ? txtWebPassword.Password
                    : txtWebPasswordVisible.Text;

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
                    $"AutoBlock={(chkAutoBlock.IsChecked == true).ToString().ToLower()}",
                    "",
                    "# --- Filtres et Whitelists ---",
                    $"IgnoredNetworks={txtIgnoredNetworks.Text.Trim()}",
                    $"DisableFilters={(chkDisableFilters.IsChecked == true).ToString().ToLower()}",
                    "",
                    "# --- Threat Intelligence ---",
                    $"BlacklistLevel={txtBlacklistLevel.Text.Trim()}",
                    $"BlacklistLastUpdate={(DateTime.TryParse(lblBlacklistLastUpdate.Text.Replace("Dernière MAJ : ", ""), out var dt) ? dt.ToUniversalTime().ToString("O") : DateTime.UtcNow.ToString("O"))}",
                    "",
                    "# --- Serveur Web ---",
                    $"WebEnabled={webEnabled}",
                    $"WebPort={webPort}",
                    $"WebUsername={webLogin}",
                    $"WebPassword={webPass}",
                    "",
                    "# --- Recherche d'IP (lien externe) ---",
                    $"IpLookupUrl={currentIpLookupUrl}"
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

        private void ChkDisableFilters_Checked(object sender, RoutedEventArgs e)
        {
            lblDisableFiltersWarning.Visibility = Visibility.Visible;
        }

        private void ChkDisableFilters_Unchecked(object sender, RoutedEventArgs e)
        {
            lblDisableFiltersWarning.Visibility = Visibility.Collapsed;
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
                MessageBox.Show(
                    "Paramètres Web mis à jour.",
                    "OK",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                await LoadWebConfig();
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

        private void OpenLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor", "Logs");

                if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);
                Process.Start("explorer.exe", logFolder);
            }
            catch (Exception ex) { MessageBox.Show("Impossible d'ouvrir le dossier Logs.\n" + ex.Message); }
        }

        private async void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? base64 = await NetMonitorIpcClient.ExportPdfAsync();
                if (string.IsNullOrEmpty(base64))
                {
                    MessageBox.Show("Erreur lors de la génération du PDF.", "NetMonitor", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                byte[] pdfBytes = Convert.FromBase64String(base64);

                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"NetMonitor_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
                    Filter = "Fichier PDF (*.pdf)|*.pdf",
                    Title = "Enregistrer le rapport PDF"
                };

                if (sfd.ShowDialog() == true)
                {
                    File.WriteAllBytes(sfd.FileName, pdfBytes);
                    MessageBox.Show("Rapport PDF enregistré avec succès.", "NetMonitor", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message);
            }
        }

        // ------------------------------------------------------------
        //  Lookup IP : ouvre AbuseIPDB (ou l'URL configurée)
        // ------------------------------------------------------------
        private void LookupIp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string? ip = btn.Tag as string;
            if (string.IsNullOrWhiteSpace(ip)) return;

            try
            {
                string template = LoadIpLookupUrl();
                string url = template.Replace("{ip}", Uri.EscapeDataString(ip));

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir le navigateur : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Lit IpLookupUrl depuis NetMonitor.config (fallback AbuseIPDB)
        private static string LoadIpLookupUrl()
        {
            const string fallback = "https://www.abuseipdb.com/check/{ip}";
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor", "NetMonitor.config");

                if (!File.Exists(configPath)) return fallback;

                foreach (var line in File.ReadAllLines(configPath))
                {
                    if (line.StartsWith("IpLookupUrl=", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = line.Substring("IpLookupUrl=".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                }
            }
            catch { }
            return fallback;
        }

        // ------------------------------------------------------------
        //  Statistiques
        // ------------------------------------------------------------
        private async void RefreshStats_Click(object sender, RoutedEventArgs e)
            => await RefreshStatsAsync();

        private async Task RefreshStatsAsync()
        {
            try
            {
                txtStatsLastUpdate.Text = "Chargement...";

                var stats = await NetMonitorIpcClient.GetStatsAsync();
                if (stats == null)
                {
                    txtStatsLastUpdate.Text = "Service non joignable";
                    return;
                }

                // --- Cartes globales ---
                txtStatsTotalAlerts.Text       = stats.TotalAlerts.ToString();
                txtStatsDistinctIps.Text       = stats.DistinctIps.ToString();
                txtStatsDistinctCountries.Text = stats.DistinctCountries.ToString();

                string worst = (stats.WorstSeverity ?? "ok").ToLowerInvariant();
                txtStatsWorstSeverity.Text = worst switch
                {
                    "critical" => "CRITIQUE",
                    "warning"  => "ALERTE",
                    _          => "OK"
                };
                txtStatsWorstSeverity.Foreground = worst switch
                {
                    "critical" => new SolidColorBrush(Color.FromRgb(0xFF, 0x63, 0x47)),
                    "warning"  => new SolidColorBrush(Color.FromRgb(0xFF, 0xB9, 0x00)),
                    _          => new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F))
                };

                // --- Section Active ---
                txtActiveSummary.Text = $"{stats.Active.TotalAlerts} alertes · " +
                                        $"{stats.Active.DistinctIps} IPs · " +
                                        $"{stats.Active.DistinctCountries} pays";

                _statsActiveIps.Clear();
                foreach (var ip in stats.Active.TopIps)
                {
                    _statsActiveIps.Add(new StatsIpRow
                    {
                        Ip = ip.Ip,
                        CountryDisplay = BuildCountryDisplay(ip.Country, ip.CountryCode),
                        SeverityDisplay = ip.MaxSeverity,
                        Count = ip.Count,
                        DetectorsDisplay = ip.Detectors.Count > 0 ? string.Join(", ", ip.Detectors) : "-",
                        LastSeen = ip.LastSeen.ToLocalTime()
                    });
                }

                _statsActiveCountries.Clear();
                foreach (var c in stats.Active.TopCountries)
                {
                    _statsActiveCountries.Add(new StatsCountryRow
                    {
                        CountryDisplay = BuildCountryDisplay(c.Country, c.CountryCode),
                        Count = c.Count,
                        DistinctIps = c.DistinctIps
                    });
                }

                _statsActiveDetectors.Clear();
                foreach (var d in stats.Active.TopDetectors)
                    _statsActiveDetectors.Add(new StatsDetectorRow { Detector = d.Detector, Count = d.Count });

                // --- Section Blocked ---
                txtBlockedSummary.Text = $"{stats.Blocked.TotalAlerts} IPs · " +
                                         $"{stats.Blocked.DistinctCountries} pays";

                _statsBlockedIps.Clear();
                foreach (var ip in stats.Blocked.TopIps)
                {
                    _statsBlockedIps.Add(new StatsIpRow
                    {
                        Ip = ip.Ip,
                        CountryDisplay = BuildCountryDisplay(ip.Country, ip.CountryCode),
                        Isp = string.IsNullOrWhiteSpace(ip.Isp) ? "-" : ip.Isp,
                        DetectorsDisplay = ip.Detectors.Count > 0 ? string.Join(", ", ip.Detectors) : "-",
                        LastSeen = ip.LastSeen.ToLocalTime()
                    });
                }

                _statsBlockedCountries.Clear();
                foreach (var c in stats.Blocked.TopCountries)
                {
                    _statsBlockedCountries.Add(new StatsCountryRow
                    {
                        CountryDisplay = BuildCountryDisplay(c.Country, c.CountryCode),
                        Count = c.Count,
                        DistinctIps = c.DistinctIps
                    });
                }

                _statsBlockedDetectors.Clear();
                foreach (var d in stats.Blocked.TopDetectors)
                    _statsBlockedDetectors.Add(new StatsDetectorRow { Detector = d.Detector, Count = d.Count });

                txtStatsLastUpdate.Text = $"Dernière mise à jour : {stats.GeneratedAt.ToLocalTime():HH:mm:ss}";
            }
            catch (Exception ex)
            {
                txtStatsLastUpdate.Text = "Erreur : " + ex.Message;
            }

            // Recharger la carte + les graphiques
            if (_mapInitialized) MapWebView?.Reload();
            if (_chartsInitialized) ChartsWebView?.Reload();
        }

        private static string BuildCountryDisplay(string? country, string? code)
        {
            if (string.IsNullOrWhiteSpace(country)) return "-";
            if (string.IsNullOrWhiteSpace(code)) return country;
            return $"{country} ({code})";
        }

        private async Task InitMapWebViewAsync()
        {
            if (_mapInitialized) return;
            if (MapWebView == null) return;

            try
            {
                var status = await NetMonitorIpcClient.GetWebStatus();
                if (status == null || !status.enabled) return;

                int port = status.port;
                string user = status.username ?? "";
                string pass = status.password ?? "";

                // Dossier user data persistant (évite les conflits si UI élevée)
                string userDataFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MCEMonitor", "WebView2");
                System.IO.Directory.CreateDirectory(userDataFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await MapWebView.EnsureCoreWebView2Async(env);

                // Injecter Authorization: Basic pour toutes les requêtes vers localhost
                string filter127 = $"http://127.0.0.1:{port}/*";
                string filterLoc = $"http://localhost:{port}/*";
                MapWebView.CoreWebView2.AddWebResourceRequestedFilter(filter127, CoreWebView2WebResourceContext.All);
                MapWebView.CoreWebView2.AddWebResourceRequestedFilter(filterLoc, CoreWebView2WebResourceContext.All);

                string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
                MapWebView.CoreWebView2.WebResourceRequested += (s, e) =>
                {
                    try { e.Request.Headers.SetHeader("Authorization", "Basic " + credentials); }
                    catch { }
                };

                // Settings sobres
                MapWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                MapWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                MapWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                MapWebView.Source = new Uri($"http://127.0.0.1:{port}/map");
                _mapInitialized = true;

                CoreLog.Write($"WebView2 : carte chargée sur port {port}");
            }
            catch (Exception ex)
            {
                CoreLog.Write("InitMapWebView : " + ex.Message);
            }
        }

        private async Task InitChartsWebViewAsync()
        {
            if (_chartsInitialized) return;
            if (ChartsWebView == null) return;

            try
            {
                var status = await NetMonitorIpcClient.GetWebStatus();
                if (status == null || !status.enabled) return;

                int port = status.port;
                string user = status.username ?? "";
                string pass = status.password ?? "";

                string userDataFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MCEMonitor", "WebView2-Charts");
                System.IO.Directory.CreateDirectory(userDataFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await ChartsWebView.EnsureCoreWebView2Async(env);

                string filter127 = $"http://127.0.0.1:{port}/*";
                string filterLoc = $"http://localhost:{port}/*";
                ChartsWebView.CoreWebView2.AddWebResourceRequestedFilter(filter127, CoreWebView2WebResourceContext.All);
                ChartsWebView.CoreWebView2.AddWebResourceRequestedFilter(filterLoc, CoreWebView2WebResourceContext.All);

                string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
                ChartsWebView.CoreWebView2.WebResourceRequested += (s, e) =>
                {
                    try { e.Request.Headers.SetHeader("Authorization", "Basic " + credentials); }
                    catch { }
                };

                ChartsWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                ChartsWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                ChartsWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                ChartsWebView.Source = new Uri($"http://127.0.0.1:{port}/charts");
                _chartsInitialized = true;

                CoreLog.Write($"WebView2 Charts : graphiques chargés sur port {port}");
            }
            catch (Exception ex)
            {
                CoreLog.Write("InitChartsWebView : " + ex.Message);
            }
        }

        // ------------------------------------------------------------
        //  Préservation du scroll dans les DataGrid au refresh
        // ------------------------------------------------------------
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private static double GetScrollOffset(DataGrid grid)
        {
            var sv = FindVisualChild<ScrollViewer>(grid);
            return sv?.VerticalOffset ?? 0;
        }

        private void RestoreScrollAsync(DataGrid grid, double offset)
        {
            if (offset <= 0) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var sv = FindVisualChild<ScrollViewer>(grid);
                sv?.ScrollToVerticalOffset(offset);
            }), DispatcherPriority.Loaded);
        }

        // ------------------------------------------------------------
        //  Préservation du scroll global (ScrollViewer de l'onglet actif)
        // ------------------------------------------------------------
        private ScrollViewer? GetActiveTabScrollViewer()
        {
            // Le contenu direct d'un TabItem scrollable EST le ScrollViewer.
            // Pas besoin de le nommer dans le XAML - SelectedContent le récupère directement.
            return MainTabControl.SelectedContent as ScrollViewer;
        }

        private double GetActiveTabScrollOffset()
        {
            return GetActiveTabScrollViewer()?.VerticalOffset ?? 0;
        }

        private void RestoreActiveTabScroll(double offset)
        {
            if (offset <= 0) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                GetActiveTabScrollViewer()?.ScrollToVerticalOffset(offset);
            }), DispatcherPriority.Loaded);
        }
    }

    // ============================================================
    //  ViewModels pour les DataGrid
    // ============================================================

    public class AlertRow
    {
        public DateTime Timestamp { get; set; }
        public string TimestampText => Timestamp.ToString("dd/MM HH:mm:ss");
        public string Severity { get; set; } = "";
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Details { get; set; }
        public string? RemoteIp { get; set; }
        public int RemotePort { get; set; }
        public int LocalPort { get; set; }
        public int Pid { get; set; }
        public string? ProcessName { get; set; }

        // Affichage Processus : "chrome.exe (1234)" ou "-"
        public string ProcessDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ProcessName)) return "-";
                if (Pid > 0) return $"{ProcessName} ({Pid})";
                return ProcessName;   // ← si Pid=0, affiche juste le nom
            }
        }

        // Affichage Détails : tronqué à 120 chars
        public string DetailsDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Details)) return "-";
                return Details.Length <= 120
                    ? Details
                    : Details.Substring(0, 117) + "…";
            }
        }
    }

    public class BlockedIpRow
    {
        public string Ip { get; set; } = "";
        public DateTime BlockedAt { get; set; }
        public string BlockedAtText => BlockedAt.ToString("dd/MM/yyyy HH:mm:ss");
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Details { get; set; }
        public string Severity { get; set; } = "-";
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

    // ============================================================
    //  ViewModels - Intrusions actives groupées
    // ============================================================
    public class GroupedAlertRow
    {
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public string LastSeenText => LastSeen.ToLocalTime().ToString("dd/MM HH:mm:ss");
        public string FirstSeenText => FirstSeen.ToLocalTime().ToString("dd/MM HH:mm:ss");
        public string Severity { get; set; } = "";
        public string Detectors { get; set; } = "";
        public string? RemoteIp { get; set; }
        public string Message { get; set; } = "";
        public int Count { get; set; }

        // ⚠ NOUVEAU
        public string RemotePortsDisplay { get; set; } = "-";
        public string? Details { get; set; }

        public string CountClass => Count >= 10 ? "c10" :
                                     Count >= 5  ? "c5"  :
                                     Count >= 2  ? "c2"  : "c1";

        public string DetailsDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Details)) return "-";
                return Details.Length <= 120
                    ? Details
                    : Details.Substring(0, 117) + "…";
            }
        }
    }

    // ============================================================
    //  ViewModels - Statistiques
    // ============================================================
    public class StatsIpRow
    {
        public string Ip { get; set; } = "";
        public string CountryDisplay { get; set; } = "-";
        public string Isp { get; set; } = "-";
        public string SeverityDisplay { get; set; } = "ok";
        public int Count { get; set; }
        public string DetectorsDisplay { get; set; } = "-";
        public DateTime LastSeen { get; set; }
        public string LastSeenText => LastSeen.ToString("dd/MM HH:mm");
    }

    public class StatsCountryRow
    {
        public string CountryDisplay { get; set; } = "";
        public int Count { get; set; }
        public int DistinctIps { get; set; }
    }

    public class StatsDetectorRow
    {
        public string Detector { get; set; } = "";
        public int Count { get; set; }
    }
}