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
    public class RomMonitorDetailViewModel : INotifyPropertyChanged
    {
        private readonly ServerEntry _server;
        private readonly DetailClient _client = new();
        private CancellationTokenSource? _cts;
        private IDispatcherTimer? _refreshTimer;

        public ServerEntry Server => _server;

        public RomMonitorDetailViewModel(ServerEntry server)
        {
            _server = server;
        }

        // ============================================================
        //  Header
        // ============================================================
        public string ServerName => _server.Name;
        public string FullUrl => _server.FullUrl;

        private string _lastCheckText = "—";
        public string LastCheckText
        {
            get => _lastCheckText;
            private set { if (_lastCheckText != value) { _lastCheckText = value; OnPropertyChanged(); } }
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
        public ObservableCollection<RomDiskItem> Disks { get; } = new();
        public ObservableCollection<RomSmartItem> Smart { get; } = new();
        public ObservableCollection<RomProblemItem> Problems { get; } = new();

        public bool HasDisks => Disks.Count > 0;
        public bool HasSmart => Smart.Count > 0;
        public bool HasProblems => Problems.Count > 0;

        // ============================================================
        //  Cycle de vie
        // ============================================================
        public async Task StartAsync()
        {
            await RefreshAsync(showSpinner: false);

            _refreshTimer = Application.Current?.Dispatcher.CreateTimer();
            if (_refreshTimer != null)
            {
                _refreshTimer.Interval = TimeSpan.FromSeconds(10); // RomMonitor scan toutes les 5 min, 10 s suffit
                _refreshTimer.Tick += async (s, e) => await RefreshAsync(showSpinner: false);
                _refreshTimer.Start();
            }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;

            _refreshTimer?.Stop();
            _refreshTimer = null;
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

                var detail = await _client.FetchRomDetailAsync(_server, ct);

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

        private void ApplyDetail(RomDetailDto d)
        {
            // ---- Header ----
            LastCheckText = d.LastCheckTime == DateTime.MinValue
                ? "—"
                : d.LastCheckTime.ToString("dd/MM HH:mm:ss");

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

            // ---- Problèmes ----
            Problems.Clear();
            foreach (var p in d.Problems)
            {
                Problems.Add(new RomProblemItem
                {
                    Severity = p.Severity,
                    SeverityColor = p.Severity == "critical"
                        ? Color.FromArgb("#FF6347")
                        : Color.FromArgb("#FFB900"),
                    Category = p.Category,
                    Target = p.Target,
                    Message = p.Message
                });
            }
            OnPropertyChanged(nameof(HasProblems));

            // ---- Disques ----
            Disks.Clear();
            foreach (var disk in d.Disks)
            {
                Color barColor;
                if (disk.FreePercent < 5) barColor = Color.FromArgb("#FF6347");
                else if (disk.FreePercent < 15) barColor = Color.FromArgb("#FFB900");
                else barColor = Color.FromArgb("#6CCB5F");

                Disks.Add(new RomDiskItem
                {
                    Name = disk.Name,
                    Label = string.IsNullOrWhiteSpace(disk.Label) ? "" : disk.Label,
                    DisplayName = string.IsNullOrWhiteSpace(disk.Label)
                        ? disk.Name
                        : $"{disk.Name}  ·  {disk.Label}",
                    FreeText = $"{disk.FreeGo:F1} Go libres / {disk.TotalGo:F1} Go",
                    FreePercentText = $"{disk.FreePercent:F1} %",
                    BarProgress = Math.Clamp(disk.FreePercent / 100.0, 0, 1),
                    BarColor = barColor
                });
            }
            OnPropertyChanged(nameof(HasDisks));

            // ---- SMART ----
            Smart.Clear();
            foreach (var s in d.Smart)
            {
                if (!s.Available) continue;

                Color statusColor = s.Status switch
                {
                    "Critical" => Color.FromArgb("#FF6347"),
                    "Warning"  => Color.FromArgb("#FFB900"),
                    "OK"       => Color.FromArgb("#6CCB5F"),
                    _          => Color.FromArgb("#888888")
                };

                // Détails spécifiques NVMe / ATA
                string details = "";

                if (string.Equals(s.Type, "nvme", StringComparison.OrdinalIgnoreCase))
                {
                    if (s.PercentageUsed.HasValue)
                        details += $"Usure : {s.PercentageUsed.Value} %  ·  ";
                    if (s.AvailableSpare.HasValue)
                        details += $"Spare : {s.AvailableSpare.Value} %";
                }
                else
                {
                    if (s.ReallocatedSectors.HasValue && s.ReallocatedSectors.Value > 0)
                        details += $"Réalloués : {s.ReallocatedSectors.Value}  ·  ";
                    if (s.PendingSectors.HasValue && s.PendingSectors.Value > 0)
                        details += $"En attente : {s.PendingSectors.Value}";
                }

                Smart.Add(new RomSmartItem
                {
                    Model = string.IsNullOrWhiteSpace(s.Model) ? s.Device : s.Model,
                    TypeText = string.IsNullOrWhiteSpace(s.Type) ? "" : s.Type.ToUpperInvariant(),
                    Status = s.Status,
                    StatusText = string.IsNullOrWhiteSpace(s.StatusReason) ? s.Status : s.StatusReason,
                    StatusColor = statusColor,
                    TemperatureText = s.Temperature.HasValue ? $"{s.Temperature.Value} °C" : "—",
                    PowerOnText = FormatPowerOnHours(s.PowerOnHours),
                    Details = details.TrimEnd(' ', '·'),
                    CapacityText = $"{s.CapacityGo:F0} Go"
                });
            }
            OnPropertyChanged(nameof(HasSmart));
        }

        private static string FormatPowerOnHours(int? hours)
        {
            if (!hours.HasValue) return "—";

            int h = hours.Value;
            if (h < 24) return $"{h} h";

            int days = h / 24;
            if (days < 365) return $"{days} j";

            int years = days / 365;
            int remDays = days % 365;
            return $"{years} an{(years > 1 ? "s" : "")} {remDays} j";
        }

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
    public class RomDiskItem
    {
        public string Name { get; set; } = "";
        public string Label { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string FreeText { get; set; } = "";
        public string FreePercentText { get; set; } = "";
        public double BarProgress { get; set; }
        public Color BarColor { get; set; } = Colors.Gray;
    }

    public class RomSmartItem
    {
        public string Model { get; set; } = "";
        public string TypeText { get; set; } = "";
        public string Status { get; set; } = "";
        public string StatusText { get; set; } = "";
        public Color StatusColor { get; set; } = Colors.Gray;
        public string TemperatureText { get; set; } = "";
        public string PowerOnText { get; set; } = "";
        public string Details { get; set; } = "";
        public string CapacityText { get; set; } = "";
    }

    public class RomProblemItem
    {
        public string Severity { get; set; } = "";
        public Color SeverityColor { get; set; } = Colors.Gray;
        public string Category { get; set; } = "";
        public string Target { get; set; } = "";
        public string Message { get; set; } = "";
    }
}