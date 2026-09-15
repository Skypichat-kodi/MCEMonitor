using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using System.Threading.Tasks;
using MediaMonitor.Core.Models;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO;
using MediaMonitor.Core.Services;
using MediaMonitor.Core.DvbViewer;
using System.Text;
using MediaMonitor.Core.Services.Engine;

namespace MediaMonitor.Core.Services
{
    public partial class MediaMonitorEngine
    {
        private readonly List<MediaUsageItem> _history = new();
        private readonly List<MediaUsageItem> _currentOpen = new();
        private readonly List<MediaUsageItem> _historyBackup = new();

        public void LoadBackup(List<MediaUsageItem> items)
        {
            _historyBackup.Clear();
            if (items != null)
                _historyBackup.AddRange(items);
        }

        public IReadOnlyList<MediaUsageItem> GetBackup() => _historyBackup;

        private readonly System.Timers.Timer _timer;
        private string _lastImage = "";
        private int _startupCycles = 0;
        private readonly DateTime _startTime = DateTime.Now;
        private readonly object _dvbLock = new();
        private List<DvbViewerClientStream> _dvbCache = new();

        private string _dvbBaseUrl = "";

        private System.Timers.Timer? _dvbTimer;

        private readonly Dictionary<string, DateTime> _openSince = new();
        private readonly object _sync = new();

        public event Action<List<MediaUsageItem>, string>? OnUpdate;
        public string DvbViewerUrl { get; set; } = "";
        public string DvbViewerUser { get; set; } = "";
        public string DvbViewerPass { get; set; } = "";
        public bool DvbViewerEnabled { get; set; } = true;
        public bool IsBackupRunning { get; set; } = false;

        public string GetUptime()
        {
            TimeSpan up = DateTime.Now - _startTime;
            return $"{(int)up.TotalHours:00}:{up.Minutes:00}:{up.Seconds:00}";
        }

        public string GetVersion()
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            return ver?.ToString() ?? "1.0.0.0";
        }

        public DateTime GetStartTime()
        {
            return _startTime;
        }

        public MediaMonitorEngine()
        {
            _timer = new System.Timers.Timer(5000);
            _timer.Elapsed += Tick;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        private void Tick(object? sender, ElapsedEventArgs e)
        {
            // ------------------------------------------------------------
            // 1) Stabilisation SMB au démarrage (évite les faux positifs)
            // ------------------------------------------------------------
            _startupCycles++;
            if (_startupCycles <= 2)
            {
                CoreLog.Write("DEBUG SMB: Ignoré (stabilisation SMB)");
                return;
            }

            try
            {
                var server = Environment.MachineName;

                // ------------------------------------------------------------
                // 2) Mise à jour DVBViewer (asynchrone, non bloquant)
                // ------------------------------------------------------------
                _ = RefreshDvbViewerAsync();

                // ------------------------------------------------------------
                // 3) Récupération SMB : sessions + fichiers ouverts
                // ------------------------------------------------------------
                var sessions = SmbSessions.GetSessions(server);
                var files = SmbOpenFiles.GetOpenFiles(server);
                CoreLog.Write($"DEBUG SMB: {files.Count} fichiers ouverts détectés.");

                // ------------------------------------------------------------
                // 4) Filtrage des fichiers multimédia valides + jointure SMB
                // ------------------------------------------------------------
                var joined =
                    from f in files
                    let ext = Path.GetExtension(f.Path).ToLower()
                    let isRealFile = File.Exists(f.Path)
                    where isRealFile
                          && (ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov" ||
                              ext == ".ts"  || ext == ".wmv" || ext == ".flv" ||
                              ext == ".mp3" || ext == ".wav" || ext == ".flac" ||
                              ext == ".aac" || ext == ".ogg" || ext == ".wma"  ||
                              ext == ".m4a" ||
                              ext == ".jpg" || ext == ".jpeg" || ext == ".png" ||
                              ext == ".gif" || ext == ".bmp"  || ext == ".webp")
                          && MediaClassifier.IsMedia(f.Path)
                    join s in sessions on f.SessionId equals s.SessionId into gj
                    from match in gj.DefaultIfEmpty()
                    select BuildItem(f, match);

                var rawList = joined.ToList();

                // ------------------------------------------------------------
                // 4 bis) Résolution DNS sur ClientName (avec cache)
                // ------------------------------------------------------------
                foreach (var item in rawList)
                {
                    try
                    {
                        if (System.Net.IPAddress.TryParse(item.ClientName, out _))
                        {
                            var (hostName, _, success) = DnsResolver.Resolve(item.ClientName);

                            string display = success ? hostName : "";

                            if (!string.IsNullOrWhiteSpace(display))
                            {
                                display = display
                                    .Replace(".home", "", StringComparison.OrdinalIgnoreCase)
                                    .Replace(".local", "", StringComparison.OrdinalIgnoreCase)
                                    .Replace(".lan", "", StringComparison.OrdinalIgnoreCase);

                                display = display.ToUpperInvariant();

                                item.ClientDisplay = display;
                            }
                            else
                            {
                                item.ClientDisplay = item.ClientName.ToUpperInvariant();
                            }
                        }
                        else
                        {
                            string display = item.ClientName;

                            display = display
                                .Replace(".home", "", StringComparison.OrdinalIgnoreCase)
                                .Replace(".local", "", StringComparison.OrdinalIgnoreCase)
                                .Replace(".lan", "", StringComparison.OrdinalIgnoreCase)
                                .ToUpperInvariant();

                            item.ClientDisplay = display;
                        }
                    }
                    catch
                    {
                        item.ClientDisplay = item.ClientName.ToUpperInvariant();
                    }
                }

                var filtered = new List<MediaUsageItem>();

                // ------------------------------------------------------------
                // 5) Stabilisation temporelle + FILTRE IMAGE FIABLE
                // ------------------------------------------------------------
                foreach (var item in rawList)
                {
                    if (!_openSince.ContainsKey(item.Path))
                        _openSince[item.Path] = DateTime.Now;

                    double seconds = (DateTime.Now - _openSince[item.Path]).TotalSeconds;

                    if (item.MediaType == "Image")
                    {
                        try
                        {
                            long size = new FileInfo(item.Path).Length;
                            if (size < 200_000)
                                continue;
                        }
                        catch { }

                        if (seconds < 1)
                            continue;
                    }

                    bool keep = item.MediaType switch
                    {
                        "Serie" => seconds >= 7,
                        "Video" => seconds >= 7,
                        "Audio" => seconds >= 10,
                        _ => seconds >= 20
                    };

                    if (keep)
                        filtered.Add(item);
                }

                CoreLog.Write($"DEBUG FILTER: {rawList.Count} bruts, {filtered.Count} après filtrage.");

                // ------------------------------------------------------------
                // 6) Nettoyage des fichiers fermés
                // ------------------------------------------------------------
                var pathsStillOpen = rawList.Select(x => x.Path).ToHashSet();
                var keys = _openSince.Keys.ToList();
                foreach (var p in keys)
                {
                    if (!pathsStillOpen.Contains(p))
                        _openSince.Remove(p);
                }

                // ------------------------------------------------------------
                // 7) Section critique : mise à jour des listes internes
                // ------------------------------------------------------------
                lock (_sync)
                {
                    if (IsBackupRunning)
                    {
                        CoreLog.Write("DEBUG BACKUP: Tick ignoré (backup en cours)");
                        return;
                    }

                    _currentOpen.Clear();
                    _currentOpen.AddRange(filtered);

                    // DVBViewer
                    var dvb = GetCachedDvbViewerStreams();

                    foreach (var s in dvb.Streams)
                    {
                        var item = BuildDvbItem(s, dvb.BaseUrl);

                        bool exists = _currentOpen.Any(x =>
                            x.ClientName == item.ClientName &&
                            x.MediaType == item.MediaType &&
                            x.Nom == item.Nom
                        );

                        if (!exists)
                            _currentOpen.Add(item);
                    }

                    // ------------------------------------------------------------
                    // 8) Dernière image
                    // ------------------------------------------------------------
                    foreach (var item in _currentOpen)
                    {
                        if (item.MediaType == "Image")
                            _lastImage = item.Path;
                    }

                    // ------------------------------------------------------------
                    // 9) Historique FIABLE
                    // ------------------------------------------------------------
                    foreach (var item in _currentOpen)
                    {
                        if (item.MediaType == "Image")
                        {
                            if (item.Path != _lastImage)
                                continue;
                        }

                        if (item.MediaType == "Audio")
                        {
                            double seconds = (DateTime.Now - _openSince[item.Path]).TotalSeconds;
                            if (seconds < 15)
                                continue;
                        }

                        bool isNew = !_history.Any(h =>
                            h.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase) &&
                            h.MediaType == item.MediaType);

                        if (isNew)
                        {
                            _history.Add(item);
                            CoreLog.Write($"HISTORY: Ajout => {item.Path} ({item.ClientDisplay})");
                        }
                    }
                }

                // ------------------------------------------------------------
                // 10) Envoi à l'interface Web
                // ------------------------------------------------------------
                OnUpdate?.Invoke(_currentOpen, _lastImage);
            }
            catch (Exception ex)
            {
                CoreLog.Write("SMB ERROR: " + ex.Message);
            }
        }

        // ============================================================
        //  Construction d'un item depuis une entrée SMB
        // ============================================================

        private MediaUsageItem BuildItem(SmbOpenFile f, SmbSession? match)
        {
            // IP brute renvoyée par Windows SMB
            string clientName = match?.ClientComputerName;
            if (string.IsNullOrWhiteSpace(clientName))
                clientName = match?.Username;
            if (string.IsNullOrWhiteSpace(clientName))
                clientName = "Inconnu";

            string ext = Path.GetExtension(f.Path).ToLower();

            int saison = 0;
            int episode = 0;
            string mediaType;

            if (ext is ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg" or ".m4a")
            {
                mediaType = "Audio";
            }
            else if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp")
            {
                mediaType = "Image";
            }
            else
            {
                MediaClassifier.ExtractEpisodeInfo(f.Path, out saison, out episode);

                mediaType = saison > 0 && episode > 0
                    ? "Serie"
                    : MediaClassifier.GetMediaType(f.Path);
            }

            string file = Path.GetFileName(f.Path);

            return new MediaUsageItem
            {
                SessionId = (uint)f.SessionId,
                ClientName = clientName,
                ClientDisplay = clientName,
                Path = f.Path,
                FileName = file,
                UNC = PathTools.ToUNC(f.Path),
                Timestamp = DateTime.Now,
                MediaType = mediaType,
                Nom = MediaNaming.CleanEpisodeName(file),
                Saison = saison,
                Episode = episode,
                Channel = ""
            };
        }

        // ============================================================
        //  Construction d'un item depuis un flux DVBViewer
        // ============================================================

        private MediaUsageItem BuildDvbItem(DvbViewerClientStream s, string dvbBaseUrl)
        {
            // Type cohérent avec WebServer
            string mediaType = s.Type.StartsWith("REC", StringComparison.OrdinalIgnoreCase)
                ? "rec"
                : "tv";

            // Canal DVBViewer
            string channel = s.Type.StartsWith("REC", StringComparison.OrdinalIgnoreCase)
                ? s.Type.Substring(3).Trim()
                : s.Type.Trim();

            // Flag : afficher le logo à la place de la miniature
            bool ifChannelLogo = mediaType == "rec" || mediaType == "tv";

            // Construction de l'URL du logo du canal
            string channelLogo = "";
            try
            {
                if (!string.IsNullOrWhiteSpace(dvbBaseUrl))
                {
                    string baseLogoUrl = dvbBaseUrl;
                    int idx = baseLogoUrl.IndexOf("/status.html", StringComparison.OrdinalIgnoreCase);
                    if (idx > 0)
                        baseLogoUrl = baseLogoUrl.Substring(0, idx);

                    string encodedChannel = Uri.EscapeDataString(channel);

                    channelLogo = $"{baseLogoUrl}/Logos/{encodedChannel}.png?height=200";
                }
            }
            catch
            {
                channelLogo = "";
            }

            // Titre brut DVBViewer
            string titreBrut = !string.IsNullOrWhiteSpace(s.Nom)
                ? s.Nom
                : channel;

            // Nettoyage + extraction Saison/Episode + SeriesName/EpisodeName
            var parsed = TvTitleParser.Parse(titreBrut);

            string titrePropre = parsed.CleanTitle;
            int saison = parsed.Saison;
            int episode = parsed.Episode;
            string seriesName = parsed.SeriesName;
            string episodeName = parsed.EpisodeName;

            string nomFinal = titrePropre;

            // Résolution IP / ClientDisplay (avec cache)
            string clientRaw = s.Client;
            string resolvedIp = clientRaw;
            string display = clientRaw;

            var (hostName, ipResolved, success) = DnsResolver.Resolve(clientRaw);

            if (success)
            {
                if (!string.IsNullOrWhiteSpace(ipResolved))
                    resolvedIp = ipResolved;

                display = hostName;
            }
            else
            {
                if (System.Net.IPAddress.TryParse(clientRaw, out _))
                {
                    resolvedIp = clientRaw;
                    display = clientRaw;
                }
                else
                {
                    var smb = _currentOpen.FirstOrDefault(x =>
                        x.ClientDisplay.Contains(clientRaw, StringComparison.OrdinalIgnoreCase));

                    if (smb != null)
                        resolvedIp = smb.ClientName;

                    display = clientRaw;
                }
            }

            display = display
                .Replace(".home", "", StringComparison.OrdinalIgnoreCase)
                .Replace(".local", "", StringComparison.OrdinalIgnoreCase)
                .Replace(".lan", "", StringComparison.OrdinalIgnoreCase)
                .ToUpperInvariant();

            return new MediaUsageItem
            {
                SessionId = 0,
                ClientName = resolvedIp,
                ClientDisplay = display,
                Path = nomFinal,
                FileName = nomFinal,
                UNC = "",
                Timestamp = DateTime.Now,
                MediaType = mediaType,
                Nom = nomFinal,
                Saison = saison,
                Episode = episode,
                Channel = channel,
                SeriesName = seriesName,
                EpisodeName = episodeName,
                IfChannelLogo = ifChannelLogo,
                ChannelLogo = channelLogo
            };
        }

        public MediaUsageItem? FindByPath(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            // LIVE
            var live = _currentOpen.FirstOrDefault(x =>
                x.Path.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                x.Nom.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (live != null) return live;

            // HISTORY
            var hist = _history.FirstOrDefault(x =>
                x.Path.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                x.Nom.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (hist != null) return hist;

            // BACKUP
            var backup = _historyBackup.FirstOrDefault(x =>
                x.Path.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                x.Nom.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (backup != null) return backup;

            return null;
        }

        public MediaUsageItem? FindRecByPath(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            // REC en cours
            var live = _currentOpen.FirstOrDefault(x =>
                x.MediaType.Equals("rec", StringComparison.OrdinalIgnoreCase) &&
                x.Nom.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (live != null) return live;

            // REC terminés (RAM)
            var hist = _history.FirstOrDefault(x =>
                x.MediaType.Equals("rec", StringComparison.OrdinalIgnoreCase) &&
                x.Nom.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (hist != null) return hist;

            // REC en backup
            var backup = _historyBackup.FirstOrDefault(x =>
                x.MediaType.Equals("rec", StringComparison.OrdinalIgnoreCase) &&
                x.Nom.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (backup != null) return backup;

            return null;
        }

        // ============================================================
        //  GETTERS POUR IPC
        // ============================================================

        public List<MediaUsageItem> GetCurrentOpenFiles()
        {
            lock (_sync)
                return new List<MediaUsageItem>(_currentOpen);
        }

        public string GetLastImage()
        {
            lock (_sync)
                return _lastImage;
        }

        public List<MediaUsageItem> GetHistory()
        {
            lock (_sync)
            {
                var cleaned = _history
                    .GroupBy(i => new { i.Path, i.MediaType, i.Nom })
                    .Select(g => g.First())
                    .ToList();

                _history.Clear();
                _history.AddRange(cleaned);

                CoreLog.Write($"DEBUG GetHistory: retourne {_history.Count} items.");

                return new List<MediaUsageItem>(_history);
            }
        }

        public (string BaseUrl, List<DvbViewerClientStream> Streams) GetCachedDvbViewerStreams()
        {
            lock (_dvbLock)
                return (_dvbBaseUrl, new List<DvbViewerClientStream>(_dvbCache));
        }

        // ============================================================
        //  RESET HISTORIQUE
        // ============================================================

        public void ClearHistory()
        {
            lock (_sync)
            {
                _history.Clear();
                _currentOpen.Clear();
                _openSince.Clear();
                _lastImage = "";
            }
        }
    }
}