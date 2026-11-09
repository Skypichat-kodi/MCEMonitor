using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MCEMonitorClient.Shared.Models;
using MCEMonitorClient.Shared.Services;
using Microsoft.Maui.Graphics;

namespace MCEMonitorClient.Maui.ViewModels
{
    public class NetMonitorDetailViewModel : INotifyPropertyChanged
    {
        private readonly ServerEntry _server;
        private readonly DetailClient _client = new();
        private CancellationTokenSource? _cts;

        private bool _isInitialLoading;
        public bool IsInitialLoading
        {
            get => _isInitialLoading;
            private set
            {
                if (_isInitialLoading != value)
                {
                    _isInitialLoading = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasLoadedOrError));
                }
            }
        }
        public bool HasLoadedOrError => !IsInitialLoading;

        public ServerEntry Server => _server;

        public NetMonitorDetailViewModel(ServerEntry server)
        {
            _server = server;
        }

        // ============================================================
        //  Header
        // ============================================================
        public string ServerName => _server.Name;
        public string FullUrl => _server.FullUrl;

        private string _lastUpdateText = "—";
        public string LastUpdateText
        {
            get => _lastUpdateText;
            private set { if (_lastUpdateText != value) { _lastUpdateText = value; OnPropertyChanged(); } }
        }

        private string _statusText = "—";
        public string StatusText
        {
            get => _statusText;
            private set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
        }

        private Color _statusColor = Color.FromArgb("#4C4C4C");
        public Color StatusColor
        {
            get => _statusColor;
            private set { if (_statusColor != value) { _statusColor = value; OnPropertyChanged(); } }
        }

        private string _countersText = "—";
        public string CountersText
        {
            get => _countersText;
            private set { if (_countersText != value) { _countersText = value; OnPropertyChanged(); } }
        }

        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set { if (_isRefreshing != value) { _isRefreshing = value; OnPropertyChanged(); } }
        }

        private string _errorMessage = "";
        public string ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }
        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        // ============================================================
        //  Collections
        // ============================================================
        public ObservableCollection<NetIntrusionItem> Active { get; } = new();
        public ObservableCollection<NetBlockedItem> Blocked { get; } = new();
        public ObservableCollection<NetRetryItem> Retries { get; } = new();
        public ObservableCollection<NetPendingItem> Pending { get; } = new();
        public ObservableCollection<NetWhitelistItem> Whitelist { get; } = new();
        public ObservableCollection<NetProblemItem> Problems { get; } = new();
        public ObservableCollection<NetTopIpItem> TopIps { get; } = new();
        public ObservableCollection<NetTopCountryItem> TopCountries { get; } = new();
        public ObservableCollection<NetTopDetectorItem> TopDetectors { get; } = new();

        public bool HasActive => Active.Count > 0;
        public bool HasNoActive => Active.Count == 0;
        public bool HasBlocked => Blocked.Count > 0;
        public bool HasNoBlocked => Blocked.Count == 0;
        public bool HasRetries => Retries.Count > 0;
        public bool HasPending => Pending.Count > 0;
        public bool HasNoPending => Pending.Count == 0;
        public bool HasWhitelist => Whitelist.Count > 0;
        public bool HasNoWhitelist => Whitelist.Count == 0;
        public bool HasProblems => Problems.Count > 0;
        public bool HasTopIps => TopIps.Count > 0;
        public bool HasTopCountries => TopCountries.Count > 0;
        public bool HasTopDetectors => TopDetectors.Count > 0;

        // ============================================================
        //  Stats summary
        // ============================================================
        private string _statsSummaryText = "—";
        public string StatsSummaryText
        {
            get => _statsSummaryText;
            private set { if (_statsSummaryText != value) { _statsSummaryText = value; OnPropertyChanged(); } }
        }

        // ============================================================
        //  Cycle de vie
        // ============================================================
        public async Task StartAsync()
        {
            IsInitialLoading = true;
            try
            {
                await RefreshAsync(showSpinner: false);
            }
            finally
            {
                IsInitialLoading = false;
            }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
        }

        public async Task RefreshManuallyAsync()
        {
            await RefreshAsync(showSpinner: true);
        }

        // ============================================================
        //  Rafraîchissement
        // ============================================================
        private async Task RefreshAsync(bool showSpinner)
        {
            try
            {
                if (showSpinner)
                    IsRefreshing = true;

                try { _cts?.Cancel(); } catch { }
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;

                var detail = await _client.FetchNetDetailAsync(_server, ct);

                if (ct.IsCancellationRequested)
                    return;

                if (detail == null)
                {
                    ErrorMessage = "Impossible de récupérer les données du serveur.";
                    StatusText = "Hors ligne";
                    StatusColor = Color.FromArgb("#888888");
                    return;
                }

                ErrorMessage = "";
                ApplyDetail(detail);
            }
            finally
            {
                if (showSpinner)
                    IsRefreshing = false;
            }
        }

        private void ApplyDetail(NetDetailDto d)
        {
            // ---- Header ----
            LastUpdateText = d.Timestamp.ToString("dd/MM HH:mm:ss");
            
            // Calculer le nombre d'IPs bloquées dans les 24 h pour le header
            var blocked24hCount = d.Blocked.Count(x => x.BlockedAt.ToUniversalTime() >= DateTime.UtcNow.AddHours(-24));

            CountersText = $"{d.ActiveCount} active(s)  ·  {blocked24hCount} bloquée(s) / 24 h  ·  {d.BlockedCount} au total";

            StatusText = d.WorstSeverity switch
            {
                "critical" => "Critique",
                "warning"  => "Avertissement",
                "ok"       => "OK",
                _          => d.WorstSeverity
            };
            StatusColor = d.WorstSeverity switch
            {
                "critical" => Color.FromArgb("#FF6347"),
                "warning"  => Color.FromArgb("#FFB900"),
                "ok"       => Color.FromArgb("#6CCB5F"),
                _          => Color.FromArgb("#888888")
            };

            // ---- Actifs ----
            Active.Clear();
            foreach (var a in d.Active.OrderByDescending(x => x.Timestamp))
            {
                Active.Add(new NetIntrusionItem
                {
                    Timestamp = a.Timestamp.ToString("HH:mm:ss"),
                    Source = a.Source,
                    Severity = a.Severity,
                    SeverityColor = SeverityColor(a.Severity),
                    Message = a.Message,
                    RemoteIp = a.RemoteIp ?? "",
                    Country = FormatLocation(a.Country, a.CountryCode),
                    Isp = a.Isp ?? "",
                    IsKnownScanner = a.IsKnownScanner
                });
            }
            OnPropertyChanged(nameof(HasActive));
            OnPropertyChanged(nameof(HasNoActive));

            // ---- Bloqués (24 dernières heures, limité aux 10 plus récents) ----
            const int MAX_BLOCKED_DISPLAYED = 10;
            var cutoff = DateTime.UtcNow.AddHours(-24);

            var blockedLast24h = d.Blocked
                .Where(x => x.BlockedAt.ToUniversalTime() >= cutoff)
                .OrderByDescending(x => x.BlockedAt)
                .ToList();

            Blocked.Clear();
            foreach (var b in blockedLast24h.Take(MAX_BLOCKED_DISPLAYED))
            {
                Blocked.Add(new NetBlockedItem
                {
                    Ip = b.Ip,
                    BlockedAtText = b.BlockedAt.ToLocalTime().ToString("dd/MM HH:mm"),
                    Source = b.Source,
                    Message = b.Message,
                    Severity = b.Severity,
                    SeverityColor = SeverityColor(b.Severity),
                    Country = FormatLocation(b.Country, b.CountryCode),
                    City = b.City ?? "",
                    Isp = b.Isp ?? "",
                    IsProcess = b.IsProcess,
                    IsKnownScanner = b.IsKnownScanner
                });
            }

            BlockedOverflowText = blockedLast24h.Count > MAX_BLOCKED_DISPLAYED
                ? $"+ {blockedLast24h.Count - MAX_BLOCKED_DISPLAYED} autre(s) IP bloquée(s) dans les 24 h"
                : "";

            OnPropertyChanged(nameof(HasBlocked));
            OnPropertyChanged(nameof(HasNoBlocked));
            OnPropertyChanged(nameof(HasBlockedOverflow));

            // ---- Retries (triés par nombre de tentatives décroissant, top 5) ----
            const int MAX_RETRIES_DISPLAYED = 5;

            Retries.Clear();
            foreach (var r in d.Retries.OrderByDescending(x => x.RetryCount).Take(MAX_RETRIES_DISPLAYED))
            {
                Retries.Add(new NetRetryItem
                {
                    Ip = r.Ip,
                    Detector = r.OriginalDetector,
                    Count = r.RetryCount,
                    LastRetryText = r.LastRetryAt.ToLocalTime().ToString("dd/MM HH:mm")
                });
            }
            OnPropertyChanged(nameof(HasRetries));

            // ---- Pending ----
            Pending.Clear();
            foreach (var p in d.Pending.OrderByDescending(x => x.LastSeen))
            {
                Pending.Add(new NetPendingItem
                {
                    ProcessName = p.ProcessName,
                    ProcessPath = p.ProcessPath,
                    Severity = p.Severity,
                    SeverityColor = SeverityColor(p.Severity),
                    TrustReason = p.TrustReason ?? "",
                    Count = p.Count,
                    RemoteIp = p.RemoteIp ?? "",
                    RemotePort = p.RemotePort,
                    LastSeenText = p.LastSeen.ToLocalTime().ToString("dd/MM HH:mm")
                });
            }
            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(HasNoPending));

            // ---- Whitelist ----
            Whitelist.Clear();
            foreach (var w in d.Whitelist.OrderBy(x => x.Name))
            {
                Whitelist.Add(new NetWhitelistItem
                {
                    Name = w.Name,
                    Path = w.Path,
                    Enabled = w.Enabled,
                    EnabledText = w.Enabled ? "Activé" : "Désactivé",
                    EnabledColor = w.Enabled ? Color.FromArgb("#6CCB5F") : Color.FromArgb("#888888")
                });
            }
            OnPropertyChanged(nameof(HasWhitelist));
            OnPropertyChanged(nameof(HasNoWhitelist));

            // ---- Problèmes ----
            Problems.Clear();
            foreach (var p in d.Problems.OrderByDescending(x => x.Timestamp))
            {
                Problems.Add(new NetProblemItem
                {
                    Timestamp = p.Timestamp.ToLocalTime().ToString("dd/MM HH:mm:ss"),
                    Severity = p.Severity,
                    SeverityColor = SeverityColor(p.Severity),
                    Category = p.Category,
                    Message = p.Message
                });
            }
            OnPropertyChanged(nameof(HasProblems));

            // ---- Stats ----
            StatsSummaryText = $"{d.Stats.TotalAlerts} alerte(s)  ·  {d.Stats.DistinctIps} IP(s)  ·  {d.Stats.DistinctCountries} pays";

            TopIps.Clear();
            foreach (var ip in d.Stats.TopIps)
            {
                TopIps.Add(new NetTopIpItem
                {
                    Ip = ip.Ip,
                    Count = ip.Count,
                    MaxSeverity = ip.MaxSeverity,
                    SeverityColor = SeverityColor(ip.MaxSeverity),
                    Location = FormatLocation(ip.Country, ip.CountryCode),
                    Isp = ip.Isp ?? ""
                });
            }
            OnPropertyChanged(nameof(HasTopIps));

            TopCountries.Clear();
            foreach (var c in d.Stats.TopCountries)
            {
                TopCountries.Add(new NetTopCountryItem
                {
                    Country = c.Country,
                    CountryCode = c.CountryCode,
                    Count = c.Count,
                    DistinctIps = c.DistinctIps
                });
            }
            OnPropertyChanged(nameof(HasTopCountries));

            TopDetectors.Clear();
            foreach (var det in d.Stats.TopDetectors)
            {
                TopDetectors.Add(new NetTopDetectorItem
                {
                    Detector = det.Detector,
                    Count = det.Count
                });
            }
            OnPropertyChanged(nameof(HasTopDetectors));
        }

        private static Color SeverityColor(string severity)
        {
            return (severity ?? "").Trim().ToLowerInvariant() switch
            {
                "critical" => Color.FromArgb("#FF6347"),
                "high"     => Color.FromArgb("#FF6347"),
                "warning"  => Color.FromArgb("#FFB900"),
                "tocheck"  => Color.FromArgb("#FFB900"),
                "safe"     => Color.FromArgb("#6CCB5F"),
                "blocked"  => Color.FromArgb("#4CC2FF"),
                "ok"       => Color.FromArgb("#6CCB5F"),
                _          => Color.FromArgb("#888888")
            };
        }

        private static string FormatLocation(string? country, string? code)
        {
            if (string.IsNullOrWhiteSpace(country)) return code ?? "";
            if (string.IsNullOrWhiteSpace(code)) return country;
            return $"{country} ({code})";
        }
        
        private string _blockedOverflowText = "";
        public string BlockedOverflowText
        {
            get => _blockedOverflowText;
            private set
            {
                if (_blockedOverflowText != value)
                {
                    _blockedOverflowText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasBlockedOverflow));
                }
            }
        }
        public bool HasBlockedOverflow => !string.IsNullOrEmpty(_blockedOverflowText);        

        // ============================================================
        //  INotifyPropertyChanged
        // ============================================================
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ============================================================
    //  Item classes
    // ============================================================
    public class NetIntrusionItem
    {
        public string Timestamp { get; set; } = "";
        public string Source { get; set; } = "";
        public string Severity { get; set; } = "";
        public Color SeverityColor { get; set; } = Colors.Gray;
        public string Message { get; set; } = "";
        public string RemoteIp { get; set; } = "";
        public string Country { get; set; } = "";
        public string Isp { get; set; } = "";
        public bool IsKnownScanner { get; set; }
    }

    public class NetBlockedItem
    {
        public string Ip { get; set; } = "";
        public string BlockedAtText { get; set; } = "";
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
        public string Severity { get; set; } = "";
        public Color SeverityColor { get; set; } = Colors.Gray;
        public string Country { get; set; } = "";
        public string City { get; set; } = "";
        public string Isp { get; set; } = "";
        public bool IsProcess { get; set; }
        public bool IsKnownScanner { get; set; }
    }

    public class NetRetryItem
    {
        public string Ip { get; set; } = "";
        public string Detector { get; set; } = "";
        public int Count { get; set; }
        public string LastRetryText { get; set; } = "";
    }

    public class NetPendingItem
    {
        public string ProcessName { get; set; } = "";
        public string ProcessPath { get; set; } = "";
        public string Severity { get; set; } = "";
        public Color SeverityColor { get; set; } = Colors.Gray;
        public string TrustReason { get; set; } = "";
        public int Count { get; set; }
        public string RemoteIp { get; set; } = "";
        public int RemotePort { get; set; }
        public string LastSeenText { get; set; } = "";
    }

    public class NetWhitelistItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool Enabled { get; set; }
        public string EnabledText { get; set; } = "";
        public Color EnabledColor { get; set; } = Colors.Gray;
    }

    public class NetProblemItem
    {
        public string Timestamp { get; set; } = "";
        public string Severity { get; set; } = "";
        public Color SeverityColor { get; set; } = Colors.Gray;
        public string Category { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class NetTopIpItem
    {
        public string Ip { get; set; } = "";
        public int Count { get; set; }
        public string MaxSeverity { get; set; } = "";
        public Color SeverityColor { get; set; } = Colors.Gray;
        public string Location { get; set; } = "";
        public string Isp { get; set; } = "";
    }

    public class NetTopCountryItem
    {
        public string Country { get; set; } = "";
        public string CountryCode { get; set; } = "";
        public int Count { get; set; }
        public int DistinctIps { get; set; }
    }

    public class NetTopDetectorItem
    {
        public string Detector { get; set; } = "";
        public int Count { get; set; }
    }
}