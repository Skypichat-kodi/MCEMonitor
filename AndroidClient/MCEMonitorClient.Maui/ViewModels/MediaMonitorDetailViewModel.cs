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
    public class MediaMonitorDetailViewModel : INotifyPropertyChanged
    {
        private readonly ServerEntry _server;
        private readonly DetailClient _client = new();
        private CancellationTokenSource? _cts;
        private IDispatcherTimer? _refreshTimer;

        public ServerEntry Server => _server;

        public MediaMonitorDetailViewModel(ServerEntry server)
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

        private string _summaryText = "—";
        public string SummaryText
        {
            get => _summaryText;
            private set { if (_summaryText != value) { _summaryText = value; OnPropertyChanged(); } }
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
        //  Filtre type
        // ============================================================
        private string _selectedFilter = "all";
        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (_selectedFilter != value)
                {
                    _selectedFilter = value;
                    OnPropertyChanged();
                    ApplyHistoryFilter();
                }
            }
        }

        // ============================================================
        //  Collections
        // ============================================================
        public ObservableCollection<MediaItemVm> Live { get; } = new();
        public ObservableCollection<MediaItemVm> HistoryFiltered { get; } = new();
        public ObservableCollection<MediaStatVm> TopSeries { get; } = new();
        public ObservableCollection<MediaStatVm> TopArtists { get; } = new();
        public ObservableCollection<MediaStatVm> TopClients { get; } = new();
        public ObservableCollection<TypeStatVm> TypeStats { get; } = new();

        private List<MediaItemVm> _allHistory = new();

        public bool HasLive => Live.Count > 0;
        public bool HasNoLive => Live.Count == 0;
        public bool HasLastImage => !string.IsNullOrEmpty(_lastImage);
        public bool HasHistory => HistoryFiltered.Count > 0;
        public bool HasNoHistory => HistoryFiltered.Count == 0;
        public bool HasTopSeries => TopSeries.Count > 0;
        public bool HasTopArtists => TopArtists.Count > 0;
        public bool HasTopClients => TopClients.Count > 0;

        // ============================================================
        //  Filtre boutons (TextColor / BackgroundColor)
        // ============================================================
        public Color FilterAllColor   => GetFilterColor("all");
        public Color FilterAudioColor => GetFilterColor("audio");
        public Color FilterSerieColor => GetFilterColor("serie");
        public Color FilterVideoColor => GetFilterColor("video");
        public Color FilterImageColor => GetFilterColor("image");
        public Color FilterTvRecColor => GetFilterColor("tvrec");

        private Color GetFilterColor(string filter)
        {
            return _selectedFilter == filter
                ? Color.FromArgb("#4CC2FF")
                : Color.FromArgb("#3C3C3C");
        }

        private Color GetFilterTextColor(string filter)
        {
            return _selectedFilter == filter
                ? Color.FromArgb("#000000")
                : Color.FromArgb("#E5E5E5");
        }

        public Color FilterAllTextColor   => GetFilterTextColor("all");
        public Color FilterAudioTextColor => GetFilterTextColor("audio");
        public Color FilterSerieTextColor => GetFilterTextColor("serie");
        public Color FilterVideoTextColor => GetFilterTextColor("video");
        public Color FilterImageTextColor => GetFilterTextColor("image");
        public Color FilterTvRecTextColor => GetFilterTextColor("tvrec");

        // ============================================================
        //  Dernière image
        // ============================================================
        private string _lastImage = "";
        public string LastImage
        {
            get => _lastImage;
            private set { if (_lastImage != value) { _lastImage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLastImage)); } }
        }

        // ============================================================
        //  Cycle de vie
        // ============================================================
        public async Task StartAsync()
        {
            await RefreshAsync(showSpinner: false);

            _refreshTimer = Application.Current?.Dispatcher.CreateTimer();
            if (_refreshTimer != null)
            {
                _refreshTimer.Interval = TimeSpan.FromSeconds(10);
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
        //  Changement de filtre
        // ============================================================
        public void SetFilter(string filter)
        {
            SelectedFilter = filter;
            OnPropertyChanged(nameof(FilterAllColor));
            OnPropertyChanged(nameof(FilterAudioColor));
            OnPropertyChanged(nameof(FilterSerieColor));
            OnPropertyChanged(nameof(FilterVideoColor));
            OnPropertyChanged(nameof(FilterImageColor));
            OnPropertyChanged(nameof(FilterTvRecColor));
            OnPropertyChanged(nameof(FilterAllTextColor));
            OnPropertyChanged(nameof(FilterAudioTextColor));
            OnPropertyChanged(nameof(FilterSerieTextColor));
            OnPropertyChanged(nameof(FilterVideoTextColor));
            OnPropertyChanged(nameof(FilterImageTextColor));
            OnPropertyChanged(nameof(FilterTvRecTextColor));
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

                var detail = await _client.FetchMediaDetailAsync(_server, ct);

                if (ct.IsCancellationRequested)
                    return;

                if (detail == null)
                {
                    ErrorMessage = "Impossible de récupérer les données du serveur.";
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

        private void ApplyDetail(MediaDetailDto d)
        {
            LastUpdateText = d.Timestamp.ToString("dd/MM HH:mm:ss");
            SummaryText = $"{d.OpenCount} en cours  ·  {d.HistoryCount} dans l'historique";
            LastImage = d.LastImage ?? "";

            // ---- Live ----
            Live.Clear();
            foreach (var item in d.Live.OrderByDescending(x => x.Timestamp))
                Live.Add(BuildItemVm(item));

            OnPropertyChanged(nameof(HasLive));
            OnPropertyChanged(nameof(HasNoLive));

            // ---- Historique ----
            _allHistory.Clear();
            foreach (var item in d.History.OrderByDescending(x => x.Timestamp))
                _allHistory.Add(BuildItemVm(item));

            ApplyHistoryFilter();

            // ---- Stats ----
            TopSeries.Clear();
            foreach (var s in d.Stats.TopSeries)
                TopSeries.Add(new MediaStatVm { Label = s.Label, CountText = s.Count.ToString() });
            OnPropertyChanged(nameof(HasTopSeries));

            TopArtists.Clear();
            foreach (var s in d.Stats.TopArtists)
                TopArtists.Add(new MediaStatVm { Label = s.Label, CountText = s.Count.ToString() });
            OnPropertyChanged(nameof(HasTopArtists));

            TopClients.Clear();
            foreach (var s in d.Stats.TopClients)
                TopClients.Add(new MediaStatVm { Label = s.Label, CountText = s.Count.ToString() });
            OnPropertyChanged(nameof(HasTopClients));

            // ---- Type stats ----
            TypeStats.Clear();
            foreach (var kv in d.Stats.ByType.OrderByDescending(x => x.Value))
            {
                TypeStats.Add(new TypeStatVm
                {
                    Label = FormatTypeLabel(kv.Key),
                    CountText = kv.Value.ToString(),
                    Color = GetTypeColor(kv.Key)
                });
            }
        }

        private void ApplyHistoryFilter()
        {
            HistoryFiltered.Clear();

            foreach (var item in _allHistory)
            {
                if (MatchesFilter(item.MediaType, _selectedFilter))
                    HistoryFiltered.Add(item);
            }

            OnPropertyChanged(nameof(HasHistory));
            OnPropertyChanged(nameof(HasNoHistory));
        }

        private static bool MatchesFilter(string mediaType, string filter)
        {
            string t = (mediaType ?? "").Trim().ToLowerInvariant();

            if (filter == "all") return true;
            if (filter == "audio") return t == "audio";
            if (filter == "serie") return t == "serie";
            if (filter == "video") return t == "video";
            if (filter == "image") return t == "image";
            if (filter == "tvrec") return t == "rec" || t == "tv";

            return true;
        }

        private static MediaItemVm BuildItemVm(MediaItemDto item)
        {
            string type = (item.MediaType ?? "").Trim();
            string typeLower = type.ToLowerInvariant();

            return new MediaItemVm
            {
                Client = item.Client,
                MediaType = type,
                Icon = GetTypeIcon(typeLower),
                TypeColor = GetTypeColor(typeLower),
                Nom = item.Nom,
                Channel = item.Channel,
                Saison = item.Saison,
                Episode = item.Episode,
                SaisonEpisodeText = (item.Saison > 0 || item.Episode > 0)
                    ? $"{item.Saison:00}x{item.Episode:00}"
                    : "",
                TimestampText = item.Timestamp.ToString("dd/MM HH:mm:ss"),
                FileName = item.FileName,
                SubInfo = BuildSubInfo(item)
            };
        }

        private static string BuildSubInfo(MediaItemDto item)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(item.Client)) parts.Add(item.Client);
            if (!string.IsNullOrWhiteSpace(item.Channel)) parts.Add(item.Channel);
            if (!string.IsNullOrWhiteSpace(item.Artist)) parts.Add(item.Artist);
            if (!string.IsNullOrWhiteSpace(item.Album)) parts.Add(item.Album);
            return string.Join("  ·  ", parts);
        }

        private static string GetTypeIcon(string typeLower) => typeLower switch
        {
            "audio" => "🎵",
            "video" => "🎬",
            "image" => "🖼️",
            "serie" => "📺",
            "rec" => "🔴",
            "tv" => "📡",
            _ => "📄"
        };

        private static Color GetTypeColor(string typeLower) => typeLower switch
        {
            "audio" => Color.FromArgb("#007ACC"),
            "serie" => Color.FromArgb("#C586C0"),
            "video" => Color.FromArgb("#D19A66"),
            "image" => Color.FromArgb("#4CAF50"),
            "rec" => Color.FromArgb("#FF4D4D"),
            "tv" => Color.FromArgb("#FFE066"),
            _ => Color.FromArgb("#888888")
        };

        private static string FormatTypeLabel(string type)
        {
            return (type ?? "").Trim().ToLowerInvariant() switch
            {
                "audio" => "Audio",
                "serie" => "Série",
                "video" => "Vidéo",
                "image" => "Image",
                "rec" => "REC",
                "tv" => "TV",
                _ => type ?? ""
            };
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
    public class MediaItemVm
    {
        public string Client { get; set; } = "";
        public string MediaType { get; set; } = "";
        public string Icon { get; set; } = "";
        public Color TypeColor { get; set; } = Colors.Gray;
        public string Nom { get; set; } = "";
        public string Channel { get; set; } = "";
        public int Saison { get; set; }
        public int Episode { get; set; }
        public string SaisonEpisodeText { get; set; } = "";
        public string TimestampText { get; set; } = "";
        public string FileName { get; set; } = "";
        public string SubInfo { get; set; } = "";
    }

    public class MediaStatVm
    {
        public string Label { get; set; } = "";
        public string CountText { get; set; } = "";
    }

    public class TypeStatVm
    {
        public string Label { get; set; } = "";
        public string CountText { get; set; } = "";
        public Color Color { get; set; } = Colors.Gray;
    }
}