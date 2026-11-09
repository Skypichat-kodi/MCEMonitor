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
    public class SystemMonitorDetailViewModel : INotifyPropertyChanged
    {
        private readonly ServerEntry _server;
        private readonly DetailClient _client = new();
        private CancellationTokenSource? _cts;

        public ServerEntry Server => _server;

        public SystemMonitorDetailViewModel(ServerEntry server)
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
        //  CPU
        // ============================================================
        private string _cpuName = "—";
        public string CpuName
        {
            get => _cpuName;
            private set { if (_cpuName != value) { _cpuName = value; OnPropertyChanged(); } }
        }

        private string _cpuUsageText = "—";
        public string CpuUsageText
        {
            get => _cpuUsageText;
            private set { if (_cpuUsageText != value) { _cpuUsageText = value; OnPropertyChanged(); } }
        }

        private double _cpuUsage;
        public double CpuUsage
        {
            get => _cpuUsage;
            private set { if (Math.Abs(_cpuUsage - value) > 0.01) { _cpuUsage = value; OnPropertyChanged(); OnPropertyChanged(nameof(CpuBarProgress)); } }
        }
        public double CpuBarProgress => Math.Clamp(_cpuUsage / 100.0, 0, 1);

        private Color _cpuBarColor = Color.FromArgb("#6CCB5F");
        public Color CpuBarColor
        {
            get => _cpuBarColor;
            private set { if (_cpuBarColor != value) { _cpuBarColor = value; OnPropertyChanged(); } }
        }

        private string _cpuTempText = "—";
        public string CpuTempText
        {
            get => _cpuTempText;
            private set { if (_cpuTempText != value) { _cpuTempText = value; OnPropertyChanged(); } }
        }

        private Color _cpuTempColor = Color.FromArgb("#B4B4B4");
        public Color CpuTempColor
        {
            get => _cpuTempColor;
            private set { if (_cpuTempColor != value) { _cpuTempColor = value; OnPropertyChanged(); } }
        }

        private string _cpuFreqText = "—";
        public string CpuFreqText
        {
            get => _cpuFreqText;
            private set { if (_cpuFreqText != value) { _cpuFreqText = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<CpuCoreItem> CpuCores { get; } = new();

        // ============================================================
        //  RAM
        // ============================================================
        private string _ramUsageText = "—";
        public string RamUsageText
        {
            get => _ramUsageText;
            private set { if (_ramUsageText != value) { _ramUsageText = value; OnPropertyChanged(); } }
        }

        private double _ramUsage;
        public double RamUsage
        {
            get => _ramUsage;
            private set { if (Math.Abs(_ramUsage - value) > 0.01) { _ramUsage = value; OnPropertyChanged(); OnPropertyChanged(nameof(RamBarProgress)); } }
        }
        public double RamBarProgress => Math.Clamp(_ramUsage / 100.0, 0, 1);

        private Color _ramBarColor = Color.FromArgb("#6CCB5F");
        public Color RamBarColor
        {
            get => _ramBarColor;
            private set { if (_ramBarColor != value) { _ramBarColor = value; OnPropertyChanged(); } }
        }

        private string _ramDetailText = "—";
        public string RamDetailText
        {
            get => _ramDetailText;
            private set { if (_ramDetailText != value) { _ramDetailText = value; OnPropertyChanged(); } }
        }

        // ============================================================
        //  GPU / Réseau / Alertes / BSOD
        // ============================================================
        public ObservableCollection<GpuItem> Gpus { get; } = new();
        public ObservableCollection<NetworkItem> Networks { get; } = new();
        public ObservableCollection<AlertItem> Alerts { get; } = new();
        public ObservableCollection<BsodItem> Bsods { get; } = new();

        public bool HasGpus => Gpus.Count > 0;
        public bool HasNetworks => Networks.Count > 0;
        public bool HasAlerts => Alerts.Count > 0;
        public bool HasBsods => Bsods.Count > 0;

        // ============================================================
        //  Cycle de vie
        // ============================================================
        public async Task StartAsync()
        {
            // Pas de timer : rafraîchissement manuel uniquement (pull-to-refresh)
            // ou à l'ouverture de la page.
            await RefreshAsync(showSpinner: false);
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

                var detail = await _client.FetchSystemDetailAsync(_server, ct);

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

        private void ApplyDetail(SystemDetailDto d)
        {
            // ---- Header ----
            LastUpdateText = d.System.LastUpdate == DateTime.MinValue
                ? "—"
                : d.System.LastUpdate.ToString("dd/MM HH:mm:ss");

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

            // ---- CPU ----
            var cpu = d.System.Cpu;
            CpuName = string.IsNullOrWhiteSpace(cpu.Name) ? "CPU" : cpu.Name;
            CpuUsage = cpu.UsagePercent;
            CpuUsageText = $"{cpu.UsagePercent:F1} %";
            CpuBarColor = ColorForPercent(cpu.UsagePercent);

            CpuTempText = cpu.Temperature.HasValue
                ? $"{cpu.Temperature.Value:F1} °C"
                : "N/A";
            CpuTempColor = TempColor(cpu.Temperature);

            CpuFreqText = cpu.FrequencyMHz.HasValue
                ? $"{cpu.FrequencyMHz.Value:F0} MHz"
                : "N/A";

            CpuCores.Clear();
            foreach (var core in cpu.Cores)
            {
                CpuCores.Add(new CpuCoreItem
                {
                    Name = core.Name,
                    UsagePercent = core.UsagePercent,
                    UsageText = $"{core.UsagePercent:F0}%",
                    BarProgress = Math.Clamp(core.UsagePercent / 100.0, 0, 1),
                    BarColor = ColorForPercent(core.UsagePercent)
                });
            }

            // ---- RAM ----
            var ram = d.System.Ram;
            RamUsage = ram.UsagePercent;
            RamUsageText = $"{ram.UsagePercent:F1} %";
            RamBarColor = ColorForPercent(ram.UsagePercent);
            RamDetailText = $"{ram.UsedGB:F1} / {ram.TotalGB:F1} Go  ·  libre : {ram.FreeGB:F1} Go";

            // ---- GPU ----
            Gpus.Clear();
            foreach (var g in d.System.Gpus)
            {
                Gpus.Add(new GpuItem
                {
                    Name = string.IsNullOrWhiteSpace(g.Name) ? "GPU" : g.Name,
                    UsageText = $"{g.UsagePercent:F0} %",
                    UsageBarProgress = Math.Clamp(g.UsagePercent / 100.0, 0, 1),
                    UsageBarColor = ColorForPercent(g.UsagePercent),
                    TempText = g.Temperature.HasValue ? $"{g.Temperature.Value:F0} °C" : "N/A",
                    TempColor = TempColor(g.Temperature),
                    VramText = (g.VramUsedMB.HasValue && g.VramTotalMB.HasValue)
                        ? $"{g.VramUsedMB.Value:F0} / {g.VramTotalMB.Value:F0} Mo"
                        : "N/A"
                });
            }
            OnPropertyChanged(nameof(HasGpus));

            // ---- Réseau ----
            Networks.Clear();
            foreach (var n in d.System.Networks)
            {
                Networks.Add(new NetworkItem
                {
                    Name = string.IsNullOrWhiteSpace(n.Name) ? "Interface" : n.Name,
                    DownloadText = $"{n.DownloadKBps:F1} Ko/s",
                    UploadText = $"{n.UploadKBps:F1} Ko/s"
                });
            }
            OnPropertyChanged(nameof(HasNetworks));

            // ---- Alertes ----
            Alerts.Clear();
            foreach (var a in d.Alerts.OrderByDescending(x => x.Timestamp).Take(20))
            {
                Alerts.Add(new AlertItem
                {
                    Timestamp = a.Timestamp.ToString("dd/MM HH:mm:ss"),
                    Severity = a.Severity,
                    SeverityColor = a.Severity == "critical"
                        ? Color.FromArgb("#FF6347")
                        : a.Severity == "warning"
                            ? Color.FromArgb("#FFB900")
                            : Color.FromArgb("#6CCB5F"),
                    Target = a.Target,
                    Message = a.Message
                });
            }
            OnPropertyChanged(nameof(HasAlerts));

            // ---- BSOD ----
            Bsods.Clear();
            foreach (var b in d.Bsods.OrderByDescending(x => x.Timestamp).Take(10))
            {
                Bsods.Add(new BsodItem
                {
                    Timestamp = b.Timestamp.ToString("dd/MM/yyyy HH:mm:ss"),
                    Code = b.BugCheckCode,
                    Name = b.BugCheckName,
                    FaultyModule = b.FaultyModule
                });
            }
            OnPropertyChanged(nameof(HasBsods));
        }

        // ============================================================
        //  Couleurs
        // ============================================================
        private static Color ColorForPercent(double percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            if (percent <= 60) return Color.FromArgb("#6CCB5F");
            if (percent <= 85) return Color.FromArgb("#FFB900");
            return Color.FromArgb("#FF6347");
        }

        private static Color TempColor(double? temp)
        {
            if (!temp.HasValue) return Color.FromArgb("#B4B4B4");
            if (temp.Value >= 85) return Color.FromArgb("#FF6347");
            if (temp.Value >= 70) return Color.FromArgb("#FFB900");
            return Color.FromArgb("#B4B4B4");
        }

        // ============================================================
        //  INotifyPropertyChanged
        // ============================================================
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CpuCoreItem
    {
        public string Name { get; set; } = "";
        public double UsagePercent { get; set; }
        public string UsageText { get; set; } = "";
        public double BarProgress { get; set; }
        public Color BarColor { get; set; } = Colors.Gray;
    }

    public class GpuItem
    {
        public string Name { get; set; } = "";
        public string UsageText { get; set; } = "";
        public double UsageBarProgress { get; set; }
        public Color UsageBarColor { get; set; } = Colors.Gray;
        public string TempText { get; set; } = "";
        public Color TempColor { get; set; } = Colors.Gray;
        public string VramText { get; set; } = "";
    }

    public class NetworkItem
    {
        public string Name { get; set; } = "";
        public string DownloadText { get; set; } = "";
        public string UploadText { get; set; } = "";
    }

    public class AlertItem
    {
        public string Timestamp { get; set; } = "";
        public string Severity { get; set; } = "";
        public Color SeverityColor { get; set; } = Colors.Gray;
        public string Target { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class BsodItem
    {
        public string Timestamp { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string FaultyModule { get; set; } = "";
    }
}