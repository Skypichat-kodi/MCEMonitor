using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
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
        // Limite d'affichage par défaut des tableaux
        private const int DisplayLimit = 12;

        private readonly ObservableCollection<GroupedAlertRow> _activeIntrusions = new();
        private readonly ObservableCollection<BlockedIpRow> _blockedIps = new();
        private readonly ObservableCollection<AlertRow> _alertsHistory = new();
        private readonly ObservableCollection<ConnectionRow> _connections = new();
        private readonly ObservableCollection<AlertRow> _auditAlerts = new();
        private readonly ObservableCollection<PendingProcessRow> _pendingProcesses = new();
        private List<PendingProcessRow> _allPendingProcesses = new();
        private bool _showAllPending = false;        
        
        // --- Stats ---
        private readonly ObservableCollection<StatsIpRow>        _statsActiveIps        = new();
        private readonly ObservableCollection<StatsCountryRow>   _statsActiveCountries  = new();
        private readonly ObservableCollection<StatsDetectorRow>  _statsActiveDetectors  = new();
        private readonly ObservableCollection<StatsIpRow>        _statsBlockedIps       = new();
        private readonly ObservableCollection<StatsCountryRow>   _statsBlockedCountries = new();
        private readonly ObservableCollection<StatsDetectorRow>  _statsBlockedDetectors = new();

        // Données complètes (pour le dépliage Voir plus)
        private List<GroupedAlertRow> _allActiveGrouped = new();
        private List<BlockedIpRow> _allBlockedIps = new();
        private List<AlertRow> _allAlertsHistory = new();
        private List<AlertRow> _allAuditAlerts = new();

        // Flags "Voir plus" par tableau
        private bool _showAllActive = false;
        private bool _showAllBlocked = false;
        private bool _showAllHistory = false;
        private bool _showAllAudit = false;

        private readonly DispatcherTimer _refreshTimer;
        private bool _isRefreshing = false;
        private DateTime _lastConfigWriteTime = DateTime.MinValue;
        private long _lastConfigSize = -1;
        private bool _loadingWebConfig = false;
        private bool _mapInitialized = false;
        private bool _chartsInitialized = false;

        // ⚠ hash pour éviter la reconstruction des intrusions actives
        private string? _lastActiveHash;
        // ⚠ offset du scroll global (ScrollViewer de l'onglet actif)
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

            ActiveIntrusionsGrid.ItemsSource            = _activeIntrusions;
            BlockedIpsGrid.ItemsSource                  = _blockedIps;
            AlertsHistoryGrid.ItemsSource               = _alertsHistory;
            ConnectionsGrid.ItemsSource                 = _connections;
            AuditGrid.ItemsSource                       = _auditAlerts;
            PendingProcessesGrid.ItemsSource            = _pendingProcesses;
            PendingProcessesGrid.PreviewMouseWheel      += DataGrid_PreviewMouseWheel;            
            StatsActiveIpsGrid.ItemsSource              = _statsActiveIps;
            StatsActiveCountriesGrid.ItemsSource        = _statsActiveCountries;
            StatsActiveDetectorsGrid.ItemsSource        = _statsActiveDetectors;
            StatsBlockedIpsGrid.ItemsSource             = _statsBlockedIps;
            StatsBlockedCountriesGrid.ItemsSource       = _statsBlockedCountries;
            StatsBlockedDetectorsGrid.ItemsSource       = _statsBlockedDetectors;
            ActiveIntrusionsGrid.PreviewMouseWheel      += DataGrid_PreviewMouseWheel;
            BlockedIpsGrid.PreviewMouseWheel            += DataGrid_PreviewMouseWheel;
            AlertsHistoryGrid.PreviewMouseWheel         += DataGrid_PreviewMouseWheel;
            AuditGrid.PreviewMouseWheel                 += DataGrid_PreviewMouseWheel;

            MapWebView.CoreWebView2InitializationCompleted += (s, e) =>
            {
                MapWebView.CoreWebView2.WebMessageReceived += (ss, ee) =>
                {
                    var msg = ee.TryGetWebMessageAsString();
                    if (msg != null && msg.StartsWith("scroll:"))
                    {
                        if (double.TryParse(msg.Substring(7),
                                            System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture,
                                            out double delta))
                        {
                            ScrollParent((int)delta);
                        }
                    }
                };
            };
            
            ChartsWebView.CoreWebView2InitializationCompleted += (s, e) =>
            {
                ChartsWebView.CoreWebView2.WebMessageReceived += (ss, ee) =>
                {
                    var msg = ee.TryGetWebMessageAsString();
                    if (msg != null && msg.StartsWith("scroll:"))
                    {
                        if (double.TryParse(msg.Substring(7),
                                            System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture,
                                            out double delta))
                        {
                            ScrollParent((int)delta);
                        }
                    }
                };
            };

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _refreshTimer.Tick += async (_, __) =>
            {
                // Si l'utilisateur a une sélection active, on saute ce tick
                // (le prochain tick dans 5 s retentera)
                if (HasActiveSelection())
                    return;

                await RefreshSafe();
            };
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

                _lastGlobalScroll = GetActiveTabScrollOffset();

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
                    var newHash = string.Join("|", active.Select(a => $"{a.remoteIp}:{a.timestamp:O}"));
                    if (newHash != _lastActiveHash)
                    {
                        _lastActiveHash = newHash;

                        string? selectedIp = (ActiveIntrusionsGrid.SelectedItem as GroupedAlertRow)?.RemoteIp;

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
                                    RemotePortsDisplay = string.Join(", ",
                                        g.Select(x => x.remotePort)
                                         .Where(p => p > 0)
                                         .Distinct()
                                         .OrderBy(p => p)
                                         .Take(5))
                                        is { Length: > 0 } s ? s : "-",
                                    Pid         = mostRecent.pid,
                                    ProcessName = mostRecent.processName,
                                    ProcessPath = mostRecent.processPath,
                                    TrustReason = mostRecent.trustReason,
                                    Details     = mostRecent.details
                                };
                            })
                            .OrderByDescending(x => SeverityRank(x.Severity))
                            .ThenByDescending(x => x.Count)
                            .ThenByDescending(x => x.LastSeen)
                            .ToList();

                        // Événements sans IP
                        foreach (var a in active.Where(x => string.IsNullOrWhiteSpace(x.remoteIp))
                                                .OrderByDescending(x => x.timestamp).Take(20))
                        {
                            grouped.Add(new GroupedAlertRow
                            {
                                RemoteIp  = null,
                                FirstSeen = a.timestamp,
                                LastSeen  = a.timestamp,
                                Severity  = a.severity ?? "Info",
                                Detectors = a.source ?? "",
                                Message   = a.message ?? "",
                                Count     = 1,
                                RemotePortsDisplay = a.remotePort > 0 ? a.remotePort.ToString() : "-",
                                Pid         = a.pid,
                                ProcessName = a.processName,
                                ProcessPath = a.processPath,
                                TrustReason = a.trustReason,
                                Details     = a.details
                            });
                        }

                        _allActiveGrouped = grouped;
                        ApplyDisplayLimit(_activeIntrusions, _allActiveGrouped, _showAllActive);
                        UpdateMoreIndicator(panelActiveMore, txtActiveMoreLabel, _allActiveGrouped.Count, _showAllActive);
                        if (!_showAllActive)
                            ActiveIntrusionsGrid.Height = CalculateDataGridHeight(_activeIntrusions.Count);

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

                // ---- Tableau 1bis : processus en attente d'examen ----
                var pending = await NetMonitorIpcClient.GetPendingProcesses();
                if (pending != null)
                {
                    // ✅ Utilise le helper générique déjà présent dans ta classe
                    RefreshPreservingSelection<PendingProcessRow>(
                        PendingProcessesGrid,
                        row => row.ProcessPath,               // clé unique : chemin complet
                        () =>
                        {
                            _allPendingProcesses = pending
                                .OrderByDescending(x => SeverityRank(x.severity))
                                .ThenByDescending(x => x.count)
                                .ThenByDescending(x => x.lastSeen)
                                .Select(p => new PendingProcessRow
                                {
                                    ProcessPath = p.processPath,
                                    ProcessName = p.processName,
                                    TrustReason = p.trustReason,
                                    RemoteIp    = p.remoteIp,
                                    RemotePort  = p.remotePort,
                                    Pid         = p.pid,
                                    FirstSeen   = p.firstSeen,
                                    LastSeen    = p.lastSeen,
                                    Count       = p.count,
                                    Severity    = p.severity,
                                    Detector    = p.detector
                                })
                                .ToList();

                            ApplyDisplayLimit(_pendingProcesses, _allPendingProcesses, _showAllPending);
                            UpdateMoreIndicator(panelPendingMore, txtPendingMoreLabel, _allPendingProcesses.Count, _showAllPending);
                            if (!_showAllPending)
                                PendingProcessesGrid.Height = CalculateDataGridHeight(_pendingProcesses.Count);
                        });
                }

                // ---- Tableau 2 : IP bloquées ----
                var blocked = await NetMonitorIpcClient.GetBlockedIps();
                if (blocked != null)
                {
                    string? key = GetSelectedBlockedKey();
                    _allBlockedIps = blocked
                        .OrderByDescending(x => x.BlockedAt)
                        .Select(b => new BlockedIpRow
                        {
                            Ip = b.Ip,
                            BlockedAt = b.BlockedAt.ToLocalTime(),
                            Source = b.Source,
                            Message = b.Message,
                            Details = b.Details,
                            Severity = b.Severity,
                            IsProcess = b.IsProcess,
                            ProcessPath = b.ProcessPath,                         
                        })
                        .ToList();

                    ApplyDisplayLimit(_blockedIps, _allBlockedIps, _showAllBlocked);
                    UpdateMoreIndicator(panelBlockedMore, txtBlockedMoreLabel, _allBlockedIps.Count, _showAllBlocked);
                    if (!_showAllBlocked)
                        BlockedIpsGrid.Height = CalculateDataGridHeight(_blockedIps.Count);
                    RestoreBlockedSelection(key);
                }

                // ---- Tableau 3 : historique ----
                var history = await NetMonitorIpcClient.GetAlerts();
                if (history != null)
                {
                    string? key = GetSelectedAlertKey(AlertsHistoryGrid);
                    _allAlertsHistory = history
                        .OrderByDescending(x => x.timestamp)
                        .Take(500)
                        .Select(a => new AlertRow
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
                            ProcessName = a.processName,
                            ProcessPath = a.processPath,
                            TrustReason = a.trustReason
                        })
                        .ToList();

                    ApplyDisplayLimit(_alertsHistory, _allAlertsHistory, _showAllHistory);
                    UpdateMoreIndicator(panelHistoryMore, txtHistoryMoreLabel, _allAlertsHistory.Count, _showAllHistory);
                    if (!_showAllHistory)
                        AlertsHistoryGrid.Height = CalculateDataGridHeight(_alertsHistory.Count);
                    RestoreAlertSelection(AlertsHistoryGrid, _alertsHistory, key);
                }

                // ---- Tableau 4 : audit (whitelist) ----
                var audit = await NetMonitorIpcClient.GetAuditHistory();
                if (audit != null)
                {
                    string? key = GetSelectedAlertKey(AuditGrid);
                    _allAuditAlerts = audit
                        .OrderByDescending(x => x.timestamp)
                        .Take(500)
                        .Select(a => new AlertRow
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
                            ProcessName = a.processName,
                            ProcessPath = a.processPath,
                            TrustReason = a.trustReason
                        })
                        .ToList();

                    ApplyDisplayLimit(_auditAlerts, _allAuditAlerts, _showAllAudit);
                    UpdateMoreIndicator(panelAuditMore, txtAuditMoreLabel, _allAuditAlerts.Count, _showAllAudit);
                    if (!_showAllAudit)
                        AuditGrid.Height = CalculateDataGridHeight(_auditAlerts.Count);
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

                RestoreScrollAsync(ActiveIntrusionsGrid, activeOffset);
                RestoreScrollAsync(BlockedIpsGrid, blockedOffset);
                RestoreScrollAsync(AlertsHistoryGrid, historyOffset);
                RestoreScrollAsync(AuditGrid, auditOffset);
                RestoreScrollAsync(ConnectionsGrid, connsOffset);

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

        // ------------------------------------------------------------
        //  Helpers "Voir plus"
        // ------------------------------------------------------------
        private void ApplyDisplayLimit<T>(
            ObservableCollection<T> target, List<T> source, bool showAll)
        {
            target.Clear();
            var items = showAll ? source : source.Take(DisplayLimit);
            foreach (var item in items) target.Add(item);
        }

        private void UpdateMoreIndicator(
            StackPanel container, TextBlock label, int total, bool expanded)
        {
            if (total <= DisplayLimit)
            {
                container.Visibility = Visibility.Collapsed;
                return;
            }
            container.Visibility = Visibility.Visible;
            label.Text = expanded
                ? $"▲  {total} / {total}"
                : $"▼  {DisplayLimit} / {total}";
        }

        private void ToggleActiveMore_Click(object sender, RoutedEventArgs e)
        {
            _showAllActive = !_showAllActive;
            ApplyDisplayLimit(_activeIntrusions, _allActiveGrouped, _showAllActive);
            UpdateMoreIndicator(panelActiveMore, txtActiveMoreLabel, _allActiveGrouped.Count, _showAllActive);

            if (_showAllActive)
            {
                ActiveIntrusionsGrid.Height = double.NaN;   // Auto
                ActiveIntrusionsGrid.MaxHeight = 700;
            }
            else
            {
                ActiveIntrusionsGrid.MaxHeight = double.PositiveInfinity;
                ActiveIntrusionsGrid.Height = CalculateDataGridHeight(_activeIntrusions.Count);
            }
        }

        private void ToggleBlockedMore_Click(object sender, RoutedEventArgs e)
        {
            _showAllBlocked = !_showAllBlocked;
            ApplyDisplayLimit(_blockedIps, _allBlockedIps, _showAllBlocked);
            UpdateMoreIndicator(panelBlockedMore, txtBlockedMoreLabel, _allBlockedIps.Count, _showAllBlocked);

            if (_showAllBlocked)
            {
                BlockedIpsGrid.Height = double.NaN;
                BlockedIpsGrid.MaxHeight = 700;
            }
            else
            {
                BlockedIpsGrid.MaxHeight = double.PositiveInfinity;
                BlockedIpsGrid.Height = CalculateDataGridHeight(_blockedIps.Count);
            }
        }

        private void ToggleHistoryMore_Click(object sender, RoutedEventArgs e)
        {
            _showAllHistory = !_showAllHistory;
            ApplyDisplayLimit(_alertsHistory, _allAlertsHistory, _showAllHistory);
            UpdateMoreIndicator(panelHistoryMore, txtHistoryMoreLabel, _allAlertsHistory.Count, _showAllHistory);

            if (_showAllHistory)
            {
                AlertsHistoryGrid.Height = double.NaN;
                AlertsHistoryGrid.MaxHeight = 700;
            }
            else
            {
                AlertsHistoryGrid.MaxHeight = double.PositiveInfinity;
                AlertsHistoryGrid.Height = CalculateDataGridHeight(_alertsHistory.Count);
            }
        }

        private void ToggleAuditMore_Click(object sender, RoutedEventArgs e)
        {
            _showAllAudit = !_showAllAudit;
            ApplyDisplayLimit(_auditAlerts, _allAuditAlerts, _showAllAudit);
            UpdateMoreIndicator(panelAuditMore, txtAuditMoreLabel, _allAuditAlerts.Count, _showAllAudit);

            if (_showAllAudit)
            {
                AuditGrid.Height = double.NaN;
                AuditGrid.MaxHeight = 700;
            }
            else
            {
                AuditGrid.MaxHeight = double.PositiveInfinity;
                AuditGrid.Height = CalculateDataGridHeight(_auditAlerts.Count);
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
        //  Restauration des sélections
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

        private async void WhitelistSelectedProcess_Click(object sender, RoutedEventArgs e)
        {
            if (PendingProcessesGrid.SelectedItem is not PendingProcessRow row) return;

            var confirm = MessageBox.Show(
                $"Ajouter ce processus à la liste blanche ?\n\n{row.ProcessPath}\n\n" +
                "Il ne sera plus jamais détecté comme non approuvé.",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            bool ok = await NetMonitorIpcClient.WhitelistProcessAsync(row.ProcessPath);
            MessageBox.Show(ok ? "Processus ajouté à la liste blanche." : "Échec de l'ajout.",
                "NetMonitor", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Error);
            await RefreshSafe();
        }

        private async void BlockSelectedProcess_Click(object sender, RoutedEventArgs e)
        {
            if (PendingProcessesGrid.SelectedItem is not PendingProcessRow row) return;

            var confirm = MessageBox.Show(
                $"Bloquer ce processus via le pare-feu Windows ?\n\n{row.ProcessPath}\n\n" +
                "Une règle sortante sera créée pour ce programme.",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            var (ok, err) = await NetMonitorIpcClient.BlockProcessWithResultAsync(row.ProcessPath);
            MessageBox.Show(ok ? "Processus bloqué." : $"Échec du blocage.\n\n{err}",
                "NetMonitor", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Error);
            await RefreshSafe();
        }

        private void TogglePendingMore_Click(object sender, RoutedEventArgs e)
        {
            _showAllPending = !_showAllPending;
            ApplyDisplayLimit(_pendingProcesses, _allPendingProcesses, _showAllPending);
            UpdateMoreIndicator(panelPendingMore, txtPendingMoreLabel, _allPendingProcesses.Count, _showAllPending);
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
        //  Lookup IP : ouvre AbuseIPDB
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

        private void LookupProcess_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string? url = btn.Tag as string;
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
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
        {
            await RefreshStatsAsync();
            // Reload uniquement sur action utilisateur explicite (pas à chaque tick)
            if (_mapInitialized)   MapWebView?.Reload();
            if (_chartsInitialized) ChartsWebView?.Reload();
        }

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

            // Ne PAS recharger les WebView ici : ça bloque la molette pendant le reload.
            // Les pages /map et /charts ont un <meta refresh> ou font leur propre fetch.
            // On force juste un rechargement des données quand l'utilisateur clique explicitement "Actualiser".
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

                string userDataFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MCEMonitor", "WebView2");
                System.IO.Directory.CreateDirectory(userDataFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await MapWebView.EnsureCoreWebView2Async(env);
                // Évite que la WebView2 avale la molette pendant le chargement
                MapWebView.NavigationStarting   += (s, e) => MapWebView.IsHitTestVisible = false;
                MapWebView.NavigationCompleted  += (s, e) => MapWebView.IsHitTestVisible = true;             

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

                MapWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                MapWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                MapWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                string jsScroll = @"
                    window.addEventListener('wheel', function(e) {
                        window.chrome.webview.postMessage('scroll:' + e.deltaY);
                        e.preventDefault();
                    }, { passive: false });
                ";
                await MapWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(jsScroll);

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
                ChartsWebView.NavigationStarting  += (s, e) => ChartsWebView.IsHitTestVisible = false;
                ChartsWebView.NavigationCompleted += (s, e) => ChartsWebView.IsHitTestVisible = true;

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

                string jsScroll = @"
                    window.addEventListener('wheel', function(e) {
                        window.chrome.webview.postMessage('scroll:' + e.deltaY);
                        e.preventDefault();
                    }, { passive: false });
                ";
                await ChartsWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(jsScroll);

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
        //  Préservation du scroll
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

        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DataGrid dg) return;

            var sv = FindVisualChild<ScrollViewer>(dg);
            if (sv == null) return;

            bool atTop    = sv.VerticalOffset <= 0;
            bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight;

            if ((e.Delta > 0 && atTop) || (e.Delta < 0 && atBottom))
            {
                e.Handled = true;

                var parent = GetActiveTabScrollViewer();
                if (parent != null)
                {
                    parent.ScrollToVerticalOffset(parent.VerticalOffset - e.Delta);
                }
            }
        }

        private void ScrollParent(int delta)
        {
            var parent = GetActiveTabScrollViewer();
            if (parent != null)
            {
                // delta positif (molette vers le bas) = offset +
                parent.ScrollToVerticalOffset(parent.VerticalOffset + delta);
            }
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

        private ScrollViewer? GetActiveTabScrollViewer()
        {
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

        private void StatsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        // ------------------------------------------------------------
        //  Calcule la hauteur d'un DataGrid selon le nombre de lignes
        // ------------------------------------------------------------
        private static double CalculateDataGridHeight(int rowCount)
        {
            const double headerHeight = 36;       // en-tête + bordure (33 + 3 de sécurité)
            const double rowHeight    = 28;       // hauteur d'une ligne (RowHeight="28")
            const double scrollbarH   = 14;       // scrollbar horizontale (+2 de sécurité)
            const double border       = 2;        // bordures du DataGrid (haut + bas)
            const double margin       = 6;        // marge de sécurité

            int visibleRows;
            if (rowCount <= 0)
                visibleRows = 1;                  // 1 ligne vide minimum
            else
                visibleRows = Math.Min(rowCount, DisplayLimit);

            return headerHeight + (visibleRows * rowHeight) + scrollbarH + border + margin;
        }
                
        private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
                OpenFileLocation(btn.Tag as string);
        }

        private void OpenFileLocation_TextClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb)
            {
                // Évite que le DataGrid ne consomme le clic pour la sélection de ligne
                e.Handled = true;
                OpenFileLocation(tb.Tag as string);
            }
        }

        private void OpenFileLocation(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                // Cas 1 : le fichier existe → le sélectionner dans l'explorateur
                if (File.Exists(path))
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                    return;
                }

                // Cas 2 : c'est un dossier → l'ouvrir
                if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", path);
                    return;
                }

                // Cas 3 : le fichier n'existe plus → ouvrir le dossier parent s'il existe
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Process.Start("explorer.exe", dir);
                    return;
                }

                // Cas 4 : rien n'existe → avertir
                MessageBox.Show(
                    $"L'emplacement n'existe pas :\n{path}",
                    "Chemin introuvable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Impossible d'ouvrir l'emplacement :\n{ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        
        private bool HasActiveSelection()
        {
            return ActiveIntrusionsGrid.SelectedItem != null
                || PendingProcessesGrid.SelectedItem  != null
                || BlockedIpsGrid.SelectedItem        != null;
        }

        /// <summary>
        /// Exécute une action de refresh en préservant la ligne sélectionnée,
        /// identifiée par une clé métier (ex. IP, chemin de fichier…).
        /// </summary>
        private static void RefreshPreservingSelection<T>(
            DataGrid grid,
            Func<T, string?> keySelector,
            Action refreshAction)
        {
            // 1. Mémoriser la clé de la ligne sélectionnée
            string? savedKey = null;
            if (grid.SelectedItem is T selected)
                savedKey = keySelector(selected);

            // 2. Rafraîchir
            refreshAction();

            // 3. Restaurer si possible
            if (string.IsNullOrEmpty(savedKey))
                return;

            if (grid.ItemsSource is null)
                return;

            foreach (var item in grid.Items)
            {
                if (item is T candidate && keySelector(candidate) == savedKey)
                {
                    grid.SelectedItem = candidate;
                    grid.ScrollIntoView(candidate);
                    break;
                }
            }
        }                                
    }

    // ============================================================
    //  ViewModels
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
        public string? ProcessPath { get; set; }
        public string? TrustReason { get; set; }

        public string ProcessDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ProcessName)) return "-";
                if (Pid > 0) return $"{ProcessName} ({Pid})";
                return ProcessName;
            }
        }

        public string PidDisplay => Pid > 0 ? Pid.ToString() : "-";

        public string ProcessNameDisplay =>
            string.IsNullOrWhiteSpace(ProcessName) ? "-" : ProcessName;

        public string ProcessPathDisplay =>
            string.IsNullOrWhiteSpace(ProcessPath) ? "-" :
            ProcessPath.Length <= 60 ? ProcessPath : "…" + ProcessPath.Substring(ProcessPath.Length - 57);

        public string TrustReasonDisplay =>
            string.IsNullOrWhiteSpace(TrustReason) ? "-" : TrustReason;

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

        public bool IsProcess { get; set; }
        public string? ProcessPath { get; set; }

        public string TypeDisplay => IsProcess ? "Processus" : "IP";
        public string IpDisplay => IsProcess ? (ProcessPath ?? Ip) : Ip;
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
        public string RemotePortsDisplay { get; set; } = "-";
        public string? Details { get; set; }

        public int Pid { get; set; }
        public string? ProcessName { get; set; }
        public string? ProcessPath { get; set; }
        public string? TrustReason { get; set; }

        public string PidDisplay => Pid > 0 ? Pid.ToString() : "-";

        public string ProcessNameDisplay =>
            string.IsNullOrWhiteSpace(ProcessName) ? "-" : ProcessName;

        public string ProcessPathDisplay =>
            string.IsNullOrWhiteSpace(ProcessPath) ? "-" :
            ProcessPath.Length <= 60 ? ProcessPath : "…" + ProcessPath.Substring(ProcessPath.Length - 57);

        public string TrustReasonDisplay =>
            string.IsNullOrWhiteSpace(TrustReason) ? "-" : TrustReason;

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
    
    public class PendingProcessRow
    {
        public string ProcessPath { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public string? TrustReason { get; set; }
        public string? RemoteIp { get; set; }
        public int RemotePort { get; set; }
        public int Pid { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int Count { get; set; }
        public string Severity { get; set; } = "";
        public string? Detector { get; set; }

        public string LastSeenText => LastSeen.ToLocalTime().ToString("dd/MM HH:mm:ss");
        public string FirstSeenText => FirstSeen.ToLocalTime().ToString("dd/MM HH:mm:ss");
        public string PidDisplay => Pid > 0 ? Pid.ToString() : "-";
        public string ProcessNameDisplay => string.IsNullOrWhiteSpace(ProcessName) ? "-" : ProcessName;
        public string ProcessPathDisplay =>
            string.IsNullOrWhiteSpace(ProcessPath) ? "-" :
            ProcessPath.Length <= 60 ? ProcessPath : "…" + ProcessPath.Substring(ProcessPath.Length - 57);
        public string TrustReasonDisplay => string.IsNullOrWhiteSpace(TrustReason) ? "-" : TrustReason;
        public string RemoteEndPoint => $"{RemoteIp}:{RemotePort}";
        public string ProcessLookupUrl =>
        string.IsNullOrWhiteSpace(ProcessName) ? "" :
        $"https://www.google.com/search?q={Uri.EscapeDataString(ProcessName)}+exe+windows+process";

        public string CountClass => Count >= 10 ? "c10" :
                                     Count >= 5  ? "c5"  :
                                     Count >= 2  ? "c2"  : "c1";
    }    
}