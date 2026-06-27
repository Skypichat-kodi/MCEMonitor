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

        public string GetUptime()
        {
            TimeSpan up = DateTime.Now - _startTime;
            return $"{(int)up.TotalHours:00}:{up.Minutes:00}:{up.Seconds:00}";
        }

        public string GetVersion()
        {
            // Tu peux mettre ce que tu veux ici
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

                  // --------------------------------------------------------
                  // ?? FILTRE IMAGE FIABLE
                  // --------------------------------------------------------
                  if (item.MediaType == "Image")
                  {
                      try
                      {
                          long size = new FileInfo(item.Path).Length;
                          if (size < 200_000)
                              continue; // miniature ? on ignore
                      }
                      catch { }

                      // 1?? Durée minimale élevée (évite Explorer / OneDrive)
                      if (seconds < 15)
                          continue;

                      // 2?? Détection de rafale (Explorer ouvre 20 images d’un coup)
                      int imagesInBurst = rawList.Count(x =>
                          x.MediaType == "Image" &&
                          Math.Abs((_openSince[x.Path] - _openSince[item.Path]).TotalSeconds) < 2);

                      if (imagesInBurst > 2)
                          continue; // rafale ? ignorée
                  }

                  // --------------------------------------------------------
                  // Délai minimum pour les autres médias
                  // --------------------------------------------------------
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
              // 6) Nettoyage des fichiers fermés (on retire leur timer)
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
                  // -----------------------------
                  // Mise à jour de "En cours"
                  // -----------------------------
                  _currentOpen.Clear();
                  _currentOpen.AddRange(filtered);

                  // -----------------------------
                  // Ajout des flux DVBViewer
                  // -----------------------------
                  var dvb = GetCachedDvbViewerStreams();
                  CoreLog.Write($"DVB: {dvb.Count} flux récupérés du cache");

                  foreach (var s in dvb)
                  {
                      CoreLog.Write($"DVB: Ajout dans _currentOpen => {s.Client} | {s.Type} | {s.Nom}");
                      _currentOpen.Add(BuildDvbItem(s));
                  }

                  // -----------------------------
                  // Historique DVBViewer (TV/REC)
                  // -----------------------------
                  foreach (var s in dvb)
                  {
                      var dvbItem = BuildDvbItem(s);

                      if (dvbItem.MediaType == "TV" || dvbItem.MediaType.StartsWith("REC"))
                      {
                          bool isNewDvb = !_history.Any(h =>
                              h.ClientName == dvbItem.ClientName &&
                              h.MediaType == dvbItem.MediaType &&
                              h.Nom == dvbItem.Nom);

                          if (isNewDvb)
                          {
                              _history.Add(dvbItem);
                              CoreLog.Write($"DEBUG HISTORY: ajout DVB => {dvbItem.ClientName} | {dvbItem.MediaType} | {dvbItem.Nom}");
                          }
                      }
                  }

                  // ------------------------------------------------------------
                  // ?? 8) MISE À JOUR FIABLE DE LA DERNIÈRE IMAGE
                  // ------------------------------------------------------------
                  foreach (var item in _currentOpen)
                  {
                      if (item.MediaType == "Image")
                      {
                          double seconds = (DateTime.Now - _openSince[item.Path]).TotalSeconds;

                          if (seconds >= 15) // même seuil que le filtre
                              _lastImage = item.Path;
                      }
                  }

                  // ------------------------------------------------------------
                  // ?? 9) Historique FIABLE (Images + Audio)
                  // ------------------------------------------------------------
                  foreach (var item in _currentOpen)
                  {
                      // ?? Images : on ne garde QUE la dernière image stable
                      if (item.MediaType == "Image")
                      {
                          if (item.Path != _lastImage)
                              continue;
                      }

                      // ?? Audio : anti pré-ouverture
                      if (item.MediaType == "Audio")
                      {
                          double seconds = (DateTime.Now - _openSince[item.Path]).TotalSeconds;
                          if (seconds < 15)
                              continue;
                      }

                      bool isNew = !_history.Any(h =>
                          h.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase) &&
                          h.ClientIP == item.ClientIP &&
                          h.MediaType == item.MediaType);

                      if (isNew)
                      {
                          _history.Add(item);
                          CoreLog.Write($"HISTORY: Ajout => {item.Path} ({item.ClientName})");
                      }
                  }
              }

            // ------------------------------------------------------------
            // 10) Envoi de la liste fusionnée à l’interface Web
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
            string clientName = match?.ClientComputerName;
            if (string.IsNullOrWhiteSpace(clientName))
                clientName = match?.Username;
            if (string.IsNullOrWhiteSpace(clientName))
                clientName = "Inconnu";

            // ?? Correction : normalisation IPv6 ? IPv4 si possible
            string ip = NormalizeIP(match?.ClientIPAddress);

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
        //  Normalisation IP (IPv6 ? IPv4 si possible)
        // ============================================================

        private string NormalizeIP(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return "0.0.0.0";

            try
            {
                var addr = System.Net.IPAddress.Parse(ip);

                // Si IPv4 mappée dans IPv6 (::ffff:x.x.x.x)
                if (addr.IsIPv4MappedToIPv6)
                    return addr.MapToIPv4().ToString();

                // IPv6 pure ? on garde telle quelle
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
            // Type propre : REC ou TV
            string mediaType = s.Type.StartsWith("REC", StringComparison.OrdinalIgnoreCase)
                ? "REC"
                : "TV";

            // Extraction du nom de la chaîne
            // Exemple : "REC France 2" ? "France 2"
            string channel = s.Type.StartsWith("REC", StringComparison.OrdinalIgnoreCase)
                ? s.Type.Substring(3).Trim()
                : s.Type; // pour TV, s.Type contient déjà la chaîne

            // Nom final : "France 2 – Complément d’enquête"
            string nomFinal = !string.IsNullOrWhiteSpace(s.Nom)
                ? $"{channel} – {s.Nom}"
                : channel;

            return new MediaUsageItem
            {
                SessionId = 0,
                ClientName = s.Client,
                ClientIP = "DVB",
                Path = nomFinal,     // tu peux laisser s.Nom si tu préfères
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
                // ?? Nettoyage des doublons SANS réassigner _history
                var cleaned = _history
                    .GroupBy(i => new { i.Path, i.ClientIP, i.MediaType, i.Nom })
                    .Select(g => g.First())
                    .ToList();

                // On vide la liste existante et on remet les éléments propres
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
            CoreLog.Write("=== Début envoi rapport automatique ===");

            try
            {
                string html = GenerateReportFromHistory();
                var cfg = EmailConfig.Load();

                CoreLog.Write($"Taille HTML totale : {html.Length} caractères");

                // Découper le HTML en lignes
                var lignes = html.Split('\n').ToList();
                CoreLog.Write($"Nombre de lignes HTML détectées : {lignes.Count}");

                // Taille d'un bloc : 200 lignes
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

                    // Ajouter un footer simple
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

