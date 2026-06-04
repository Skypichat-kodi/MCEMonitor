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
using System.Text.Json;

namespace MediaMonitor.Core.Services
{
    public class MediaMonitorEngine
    {
        private readonly List<MediaUsageItem> _history = new();
        private readonly List<MediaUsageItem> _currentOpen = new();
        private readonly System.Timers.Timer _timer;
        private string _lastImage = "";

        private readonly Dictionary<string, DateTime> _openSince = new();
        private readonly object _sync = new();

        public event Action<List<MediaUsageItem>, string>? OnUpdate;

        public MediaMonitorEngine()
        {
            _timer = new System.Timers.Timer(5000);
            _timer.Elapsed += Tick;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        private void Tick(object? sender, ElapsedEventArgs e)
        {
            try
            {
                var server = Environment.MachineName;

                var sessions = SmbSessions.GetSessions(server);
                var files = SmbOpenFiles.GetOpenFiles(server);

                var joined =
                    from f in files
                    let ext = Path.GetExtension(f.Path).ToLower()
                    where ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov" ||
                          ext == ".ts"  || ext == ".wmv" || ext == ".flv" ||

                          ext == ".mp3" || ext == ".wav" || ext == ".flac" ||
                          ext == ".aac" || ext == ".ogg" || ext == ".wma"  ||
                          ext == ".m4a" ||

                          ext == ".jpg" || ext == ".jpeg" || ext == ".png" ||
                          ext == ".gif" || ext == ".bmp"  || ext == ".webp"

                    where MediaClassifier.IsMedia(f.Path)
                    join s in sessions on f.SessionId equals s.SessionId into gj
                    from match in gj.DefaultIfEmpty()
                    select BuildItem(f, match);

                var rawList = joined.ToList();
                var filtered = new List<MediaUsageItem>();

                foreach (var item in rawList)
                {
                    if (!_openSince.ContainsKey(item.Path))
                        _openSince[item.Path] = DateTime.Now;

                    double seconds = (DateTime.Now - _openSince[item.Path]).TotalSeconds;

                    // Filtrer miniatures
                    if (item.MediaType == "Image")
                    {
                        try
                        {
                            long size = new FileInfo(item.Path).Length;
                            if (size < 200_000)
                                continue;
                        }
                        catch { }
                    }

                    bool keep = item.MediaType switch
                    {
                        "Image" => seconds >= 5,
                        "Serie" => seconds >= 7,
                        "Video" => seconds >= 7,
                        "Audio" => seconds >= 7,
                        _ => seconds >= 20
                    };

                    if (keep)
                        filtered.Add(item);
                }

                var pathsStillOpen = rawList.Select(x => x.Path).ToHashSet();
                var keys = _openSince.Keys.ToList();
                foreach (var p in keys)
                {
                    if (!pathsStillOpen.Contains(p))
                        _openSince.Remove(p);
                }

                lock (_sync)
                {
                    _currentOpen.Clear();
                    _currentOpen.AddRange(filtered);

                    foreach (var item in filtered)
                    {
                        if (item.MediaType == "Image" && item.Path != _lastImage)
                            continue;

                        bool isNew = !_history.Any(h =>
                            h.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase) &&
                            h.ClientIP == item.ClientIP);

                        if (isNew)
                        {
                            _history.Add(item);

                            // ?? remplacé LogService ? CoreLog
                            CoreLog.Write($"Nouveau : {item.Path} ({item.ClientName})");
                        }
                    }
                }

                foreach (var item in filtered)
                {
                    if (item.MediaType == "Image")
                        _lastImage = item.Path;
                }

                OnUpdate?.Invoke(filtered, _lastImage);
            }
            catch (Exception ex)
            {
                // ?? remplacé LogService ? CoreLog
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
            string clientName = match?.ClientComputerName;
            if (string.IsNullOrWhiteSpace(clientName))
                clientName = match?.Username;
            if (string.IsNullOrWhiteSpace(clientName))
                clientName = "Inconnu";

            string ip = match?.ClientIPAddress;
            if (string.IsNullOrWhiteSpace(ip))
                ip = "0.0.0.0";

            string ext = Path.GetExtension(f.Path).ToLower();

            int saison = 0;
            int episode = 0;
            string mediaType;

            if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" ||
                ext == ".gif" || ext == ".bmp" || ext == ".webp")
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
                ClientIP = ip,
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
                return new List<MediaUsageItem>(_history);
        }

        // ============================================================
        //  RAPPORT + EMAIL
        // ============================================================

        public string GenerateReportFromHistory()
        {
            lock (_sync)
            {
                if (_history.Count == 0)
                    return "<html><body><h2>Aucun fichier ouvert depuis le démarrage du service.</h2></body></html>";

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
<h2>Historique MediaMonitor</h2>
<table>
<tr>
<th>Heure</th>
<th>Client IP</th>
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
                      $"<td>{item.ClientName}</td>" +   // IP réelle
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
            try
            {
                string html = GenerateReportFromHistory();
                var cfg = EmailConfig.Load();

                // Découper le HTML en lignes
                var lignes = html.Split('\n').ToList();

                // Taille d'un bloc : 200 lignes
                int blocTaille = 200;
                int totalBlocs = (int)Math.Ceiling(lignes.Count / (double)blocTaille);

                for (int i = 0; i < totalBlocs; i++)
                {
                    var bloc = lignes
                        .Skip(i * blocTaille)
                        .Take(blocTaille)
                        .ToList();

                    // Ajouter un footer simple
                    bloc.Add($"<br><div style='font-size:12px;color:#888;'>Partie {i + 1} / {totalBlocs}</div>");

                    string htmlBloc = string.Join("\n", bloc);

                    string sujet = totalBlocs == 1
                        ? "Rapport MediaMonitor"
                        : $"Rapport MediaMonitor (partie {i + 1}/{totalBlocs})";

                    await EmailSender.SendAsync(cfg, sujet, htmlBloc, isHtml: true);

                    CoreLog.Write($"Email automatique envoyé : bloc {i + 1}/{totalBlocs}");
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur envoi email automatique : " + ex.ToString());
            }
        }
        
        public void SaveHistoryBackup()
        {
            string folder = @"C:\ProgramData\MCEMonitor\Backups";
            Directory.CreateDirectory(folder);

            var history = GetHistory();
            string file = Path.Combine(folder, $"history_{DateTime.Now:yyyyMMdd_HHmmss}.json");

            string json = JsonSerializer.Serialize(history, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(file, json);
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

