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

namespace MediaMonitor.Core.Services
{
    public partial class MediaMonitorEngine
    {
        private readonly List<MediaUsageItem> _history = new();
        private readonly List<MediaUsageItem> _currentOpen = new();
        private readonly System.Timers.Timer _timer;
        private string _lastImage = "";
        private int _startupCycles = 0;
        private readonly DateTime _startTime = DateTime.Now;
        private readonly object _dvbLock = new();
        private List<DvbViewerClientStream> _dvbCache = new();
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
            return "1.0.0";
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
                // 4 bis) Résolution DNS sur ClientName (si c'est une IP)
                // ------------------------------------------------------------
                foreach (var item in rawList)
                {
                    try
                    {
                        if (System.Net.IPAddress.TryParse(item.ClientName, out _))
                        {
                            var entry = System.Net.Dns.GetHostEntry(item.ClientName);

                            string display = entry.HostName;

                            if (!string.IsNullOrWhiteSpace(display))
                            {
                                // ? suppression suffixes DNS
                                display = display
                                    .Replace(".home", "", StringComparison.OrdinalIgnoreCase)
                                    .Replace(".local", "", StringComparison.OrdinalIgnoreCase)
                                    .Replace(".lan", "", StringComparison.OrdinalIgnoreCase);

                                // ? majuscules
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
                            // Pas une IP ? SMB/Username
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
                        // DNS échoue ? fallback IP brute en majuscules
                        item.ClientDisplay = item.ClientName.ToUpperInvariant();
                    }
                }

                var filtered = new List<MediaUsageItem>();

                CoreLog.Write($"DEBUG FILTER: {rawList.Count} bruts, {filtered.Count} après filtrage.");

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
                    CoreLog.Write($"DVB: {dvb.Count} flux récupérés du cache");

                    foreach (var s in dvb)
                    {
                        _currentOpen.Add(BuildDvbItem(s));
                    }

                    foreach (var s in dvb)
                    {
                        var item = BuildDvbItem(s);

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
                // 10) Envoi à l’interface Web
                // ------------------------------------------------------------
                OnUpdate?.Invoke(_currentOpen, _lastImage);
            }
            catch (Exception ex)
            {
                CoreLog.Write("SMB ERROR: " + ex.Message);
            }
        }

        // ============================================================
        //  Nettoyage du nom
        // ============================================================

        private string CleanEpisodeName(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);

            name = Regex.Replace(name, @"\b(S?\d{1,2}[xE]\d{1,2})\b", "", RegexOptions.IgnoreCase);

            name = name.Replace("  ", " ");
            name = name.Replace(" -  - ", " - ");
            name = name.Replace(" -  ", " - ");
            name = name.Replace("  - ", " - ");
            name = name.Replace("-  -", "-");
            name = name.Replace("- -", "-");

            name = Regex.Replace(name, @"\s*-\s*", " - ");

            name = name.Trim();
            name = name.ToLower();
            name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);

            return name;
        }

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
                    : "Film";
            }

            string file = Path.GetFileName(f.Path);

            return new MediaUsageItem
            {
                SessionId = (uint)f.SessionId,

                // ? ClientName = IP brute SMB
                ClientName = clientName,

                // ? ClientDisplay = sera remplacé par DNS dans Tick()
                ClientDisplay = clientName,

                Path = f.Path,
                FileName = file,
                UNC = PathTools.ToUNC(f.Path),
                Timestamp = DateTime.Now,
                MediaType = mediaType,
                Nom = CleanEpisodeName(file),
                Saison = saison,
                Episode = episode
            };
        }

        // ============================================================
        //  Normalisation IP
        // ============================================================

        private string NormalizeIP(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return "0.0.0.0";

            try
            {
                var addr = System.Net.IPAddress.Parse(ip);

                if (addr.IsIPv4MappedToIPv6)
                    return addr.MapToIPv4().ToString();

                return ip;
            }
            catch
            {
                return ip ?? "0.0.0.0";
            }
        }

        private async Task RefreshDvbViewerAsync()
        {
            try
            {
                var streams = await GetDvbViewerStreamsAsync();

                lock (_dvbLock)
                    _dvbCache = streams;

                CoreLog.Write($"DVBViewer: cache mis à jour ({streams.Count} lignes).");
            }
            catch (Exception ex)
            {
                CoreLog.Write("DVBViewer refresh ERROR: " + ex.Message);
            }
        }

        private MediaUsageItem BuildDvbItem(DvbViewerClientStream s)
        {
            string mediaType = s.Type.StartsWith("REC", StringComparison.OrdinalIgnoreCase)
                ? "REC"
                : "TV";

            string channel = s.Type.StartsWith("REC", StringComparison.OrdinalIgnoreCase)
                ? s.Type.Substring(3).Trim()
                : s.Type;

            string nomFinal = !string.IsNullOrWhiteSpace(s.Nom)
                ? $"{channel} – {s.Nom}"
                : channel;

            string clientRaw = s.Client;
            string resolvedIp = clientRaw;
            string display = clientRaw;

            // ------------------------------------------------------------
            // 1) Si Client est une IP ? DNS ? IP résolue
            // ------------------------------------------------------------
            if (System.Net.IPAddress.TryParse(clientRaw, out _))
            {
                try
                {
                    var entry = System.Net.Dns.GetHostEntry(clientRaw);

                    var ip = entry.AddressList
                        .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                    if (ip != null)
                        resolvedIp = ip.ToString();

                    display = entry.HostName;
                }
                catch
                {
                    resolvedIp = clientRaw;
                    display = clientRaw;
                }
            }
            else
            {
                // ------------------------------------------------------------
                // 2) Si Client est un hostname ? DNS ? IP
                // ------------------------------------------------------------
                try
                {
                    var entry = System.Net.Dns.GetHostEntry(clientRaw);

                    var ip = entry.AddressList
                        .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                    if (ip != null)
                        resolvedIp = ip.ToString();

                    display = entry.HostName;
                }
                catch
                {
                    // ------------------------------------------------------------
                    // 3) Si Client est un nom arbitraire ? tentative via SMB
                    // ------------------------------------------------------------
                    var smb = _currentOpen.FirstOrDefault(x =>
                        x.ClientDisplay.Contains(clientRaw, StringComparison.OrdinalIgnoreCase));

                    if (smb != null)
                        resolvedIp = smb.ClientName;

                    display = clientRaw;
                }
            }

            // ------------------------------------------------------------
            // 4) Nettoyage du nom (ClientDisplay)
            // ------------------------------------------------------------
            display = display
                .Replace(".home", "", StringComparison.OrdinalIgnoreCase)
                .Replace(".local", "", StringComparison.OrdinalIgnoreCase)
                .Replace(".lan", "", StringComparison.OrdinalIgnoreCase)
                .ToUpperInvariant();

            return new MediaUsageItem
            {
                SessionId = 0,

                // ? IP brute ou résolue
                ClientName = resolvedIp,

                // ? Nom propre en MAJUSCULE
                ClientDisplay = display,

                Path = nomFinal,
                FileName = nomFinal,
                UNC = "",
                Timestamp = DateTime.Now,
                MediaType = mediaType,
                Nom = nomFinal,
                Saison = 0,
                Episode = 0
            };
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

        public List<DvbViewerClientStream> GetCachedDvbViewerStreams()
        {
            lock (_dvbLock)
                return new List<DvbViewerClientStream>(_dvbCache);
        }

        public string GenerateReportFromHistory()
        {
            lock (_sync)
            {
                if (_history.Count == 0)
                    return "<html><body><h2>Aucun fichier ouvert depuis le démarrage du service.</h2></body></html>";

                int totalMedias = _history.Count;
                int mediasParPage = 200;
                int totalPages = (int)Math.Ceiling(totalMedias / (double)mediasParPage);

                var countByType = _history
                    .GroupBy(i => i.MediaType)
                    .ToDictionary(g => g.Key, g => g.Count());

                string statsHtml = $@"
        <div style='padding:15px;background:#eef5fb;border-radius:6px;margin-bottom:20px'>
            <h3 style='margin:0;color:#2980b9'>Résumé du rapport</h3>
            <p style='margin:8px 0'>
                <b>Total médias :</b> {totalMedias}<br>
                <b>Nombre de pages :</b> {totalPages}<br>
                <b>Médias par page :</b> {mediasParPage}
            </p>
            <ul style='margin:0;padding-left:20px;color:#555'>
        ";

                foreach (var kv in countByType)
                    statsHtml += $"<li><b>{kv.Key}</b> : {kv.Value}</li>";

                statsHtml += "</ul></div>";

                var html = @"
        <html>
        <head>
        <meta charset='UTF-8'>
        <style>
        body { font-family: Arial; }
        table { border-collapse: collapse; width: 100%; }
        th, td { border: 1px solid #ccc; padding: 6px; text-align: left; }
        th { background: #eee; }
        </style>
        </head>
        <body>
        " + statsHtml + @"
        <h2>Historique MediaMonitor</h2>
        <table>
        <tr>
        <th>Heure</th>
        <th>Client</th>
        <th>Type</th>
        <th>Nom</th>
        <th>Saison</th>
        <th>Episode</th>
        <th>Fichier</th>
        <th>Chemin</th>
        </tr>
        ";

                foreach (var item in _history)
                {
                    html += "<tr>" +
                      $"<td>{item.Timestamp:HH:mm:ss}</td>" +
                      $"<td>{item.ClientDisplay}</td>" +   // ? Correction
                      $"<td>{item.MediaType}</td>" +
                      $"<td>{item.Nom}</td>" +
                      $"<td>{item.Saison}</td>" +
                      $"<td>{item.Episode}</td>" +
                      $"<td>{item.FileName}</td>" +
                      $"<td>{item.Path}</td>" +
                      "</tr>";
                }

                html += "</table></body></html>";
                return html;
            }
        }

        public async Task SendReportEmail()
        {
            CoreLog.Write("=== Début envoi rapport automatique ===");

            try
            {
                string html = GenerateReportFromHistory();
                var cfg = EmailConfig.Load();

                CoreLog.Write($"Taille HTML totale : {html.Length} caractères");

                var lignes = html.Split('\n').ToList();
                CoreLog.Write($"Nombre de lignes HTML détectées : {lignes.Count}");

                int blocTaille = 200;
                int totalBlocs = (int)Math.Ceiling(lignes.Count / (double)blocTaille);

                CoreLog.Write($"Nombre total de blocs prévus : {totalBlocs}");

                for (int i = 0; i < totalBlocs; i++)
                {
                    CoreLog.Write($"--- Préparation du bloc {i + 1}/{totalBlocs} ---");

                    var bloc = lignes
                        .Skip(i * blocTaille)
                        .Take(blocTaille)
                        .ToList();

                    CoreLog.Write($"Bloc {i + 1} : {bloc.Count} lignes");

                    bloc.Add($"<br><div style='font-size:12px;color:#888;'>Partie {i + 1} / {totalBlocs}</div>");

                    string htmlBloc = string.Join("\n", bloc);

                    CoreLog.Write($"Taille HTML du bloc {i + 1} : {htmlBloc.Length} caractères");

                    string sujet = totalBlocs == 1
                        ? "Rapport MediaMonitor"
                        : $"Rapport MediaMonitor (partie {i + 1}/{totalBlocs})";

                    CoreLog.Write($"Sujet du mail : {sujet}");
                    CoreLog.Write($"Envoi du bloc {i + 1}/{totalBlocs}...");

                    await EmailSender.SendAsync(cfg, sujet, htmlBloc, isHtml: true);

                    CoreLog.Write($"Bloc {i + 1}/{totalBlocs} envoyé avec succès.");
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur envoi email automatique : " + ex.ToString());
            }

            CoreLog.Write("=== Fin envoi rapport automatique ===");
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
