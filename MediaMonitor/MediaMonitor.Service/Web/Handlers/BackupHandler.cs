using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using MediaMonitor.Core.Language;
using MediaMonitor.Core.Models;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web.Models;

namespace MediaMonitor.Service.Web.Handlers
{
    public class BackupHandler
    {
        private readonly MediaMonitorEngine _engine;

        public BackupHandler(MediaMonitorEngine engine)
        {
            _engine = engine;
        }

        public string Render(BackupRequest req)
        {
            const string folder = @"C:\ProgramData\MCEMonitor\Backups";

            if (!Directory.Exists(folder))
                return RenderNoBackupMessage("{{tr:Aucune sauvegarde trouvée}}");

            var files = Directory.GetFiles(folder, "history_*.json");
            if (files.Length == 0)
                return RenderNoBackupMessage("{{tr:Aucune sauvegarde disponible}}");

            string lastFile = files.OrderByDescending(f => f).First();
            string json = File.ReadAllText(lastFile);

            BackupFileModel? backup = JsonSerializer.Deserialize<BackupFileModel>(json);
            if (backup == null || backup.Reports == null)
                return "<html><body><h2>Sauvegarde invalide.</h2></body></html>";

            // Fusion des rapports du même jour
            backup.Reports = backup.Reports
                .GroupBy(r => r.Date.Date)
                .Select(g => new DailyReport
                {
                    Date = g.Key,
                    Items = g.SelectMany(r => r.Items ?? new List<MediaUsageItem>()).ToList()
                })
                .ToList();

            var allItems = backup.Reports
                .Where(r => r.Items != null)
                .SelectMany(r => r.Items)
                .ToList();

            _engine.LoadBackup(allItems);   // ? plus de conversion

            var items = ApplyFilters(allItems, req);

            int total    = items.Count;
            int audio    = items.Count(i => i.MediaType.Equals("Audio", StringComparison.OrdinalIgnoreCase));
            int series   = items.Count(i => i.MediaType.Equals("Serie", StringComparison.OrdinalIgnoreCase));
            int videos   = items.Count(i => i.MediaType.Equals("Video", StringComparison.OrdinalIgnoreCase));
            int recCount = items.Count(i => i.MediaType.Equals("REC",   StringComparison.OrdinalIgnoreCase));
            int tvCount  = items.Count(i => i.MediaType.Equals("TV",    StringComparison.OrdinalIgnoreCase));

            var allClients = allItems
                .Where(i => !string.IsNullOrWhiteSpace(i.ClientDisplay))
                .Select(i => i.ClientDisplay)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            // Lignes du tableau principal
            var rows = new StringBuilder();
            foreach (var item in items)
            {
                string badgeClass = ViewHelpers.GetTypeBadgeClass(item.MediaType);
                string canal = item.Channel ?? "";
                string titreAffiche = item.Nom ?? "";
                int saisonAffiche = item.Saison;
                int episodeAffiche = item.Episode;

                rows.Append($@"
                    <tr>
                        <td style=""text-align:center;"">
                            <span class=""type-badge {badgeClass}"">{WebUtility.HtmlEncode(item.MediaType)}</span>
                        </td>
                        <td style=""text-align:left;"">{WebUtility.HtmlEncode(titreAffiche)}</td>
                        <td>{(saisonAffiche > 0 ? saisonAffiche.ToString() : "")}</td>
                        <td>{(episodeAffiche > 0 ? episodeAffiche.ToString() : "")}</td>
                        <td>{WebUtility.HtmlEncode(canal)}</td>
                        <td>{WebUtility.HtmlEncode(item.ClientDisplay ?? "")}</td>
                        <td>{item.Timestamp:dd/MM/yyyy HH:mm}</td>
                        <td style=""text-align:right;"">
                            <a class=""info-btn"" href=""#""
                               data-path=""{WebUtility.HtmlEncode(item.Path)}""
                               onclick=""openInfo(this.dataset.path)"">I</a>
                        </td>
                    </tr>");
            }

            var hoursJson = BuildHoursJson(items);

            var filteredClients = allClients
                .Where(c => !c.StartsWith("DVB-T", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var clientMediaData = new List<object>();
            foreach (var clientName in filteredClients)
            {
                var clientItems = items.Where(i =>
                    i.ClientDisplay != null &&
                    i.ClientDisplay.Equals(clientName, StringComparison.OrdinalIgnoreCase));

                clientMediaData.Add(new
                {
                    client = clientName,
                    audio = clientItems.Count(i => i.MediaType.Equals("Audio", StringComparison.OrdinalIgnoreCase)),
                    serie = clientItems.Count(i => i.MediaType.Equals("Serie", StringComparison.OrdinalIgnoreCase)),
                    video = clientItems.Count(i => i.MediaType.Equals("Video", StringComparison.OrdinalIgnoreCase)),
                    rec   = clientItems.Count(i => i.MediaType.Equals("REC",   StringComparison.OrdinalIgnoreCase)),
                    tv    = clientItems.Count(i => i.MediaType.Equals("TV",    StringComparison.OrdinalIgnoreCase))
                });
            }

            string clientMediaJson = JsonSerializer.Serialize(clientMediaData);

            var topSeries   = GetTopSeries(allItems);
            var topArtistes = GetTopArtistes(allItems);
            var topClients  = GetTopClientsStats(allItems);
            var mediaStats  = GetMediaStatsPerClient(allItems);
            var mediaStatsHtml = BuildMediaStatsPerClientHtml(mediaStats);

            var settings = WebServerSettings.Load();

            var model = new Dictionary<string, object?>
            {
                ["RETENTION_DAYS"] = settings.RetentionDays,
                ["TOTAL"]   = total,
                ["AUDIO"]   = audio,
                ["SERIES"]  = series,
                ["VIDEOS"]  = videos,
                ["REC"]     = recCount,
                ["TV"]      = tvCount,
                ["COUNT"]   = total,
                ["PERIOD"]  = WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(lastFile)),
                ["ROWS"]    = rows.ToString(),
                ["CLIENT_OPTIONS"] = BuildClientOptions(allClients, req.Client),

                ["FILTER_TYPE"]   = req.Type,
                ["FILTER_CLIENT"] = req.Client,
                ["DATE"]          = req.Date,
                ["SORT"]          = req.Sort,

                ["SEL_ALL"]   = req.Type == "all"   ? "selected" : "",
                ["SEL_AUDIO"] = req.Type == "audio" ? "selected" : "",
                ["SEL_SERIE"] = req.Type == "serie" ? "selected" : "",
                ["SEL_VIDEO"] = req.Type == "video" ? "selected" : "",

                ["SEL_DATE_ALL"]       = req.Date == "all"       ? "selected" : "",
                ["SEL_DATE_TODAY"]     = req.Date == "today"     ? "selected" : "",
                ["SEL_DATE_YESTERDAY"] = req.Date == "yesterday" ? "selected" : "",
                ["SEL_DATE_7"]         = req.Date == "7"         ? "selected" : "",
                ["SEL_DATE_30"]        = req.Date == "30"        ? "selected" : "",

                ["SEL_DATEDESC"] = req.Sort == "date_desc" ? "selected" : "",
                ["SEL_DATEASC"]  = req.Sort == "date_asc"  ? "selected" : "",
                ["SEL_NAMEASC"]  = req.Sort == "name_asc"  ? "selected" : "",
                ["SEL_NAMEDESC"] = req.Sort == "name_desc" ? "selected" : "",

                ["HOURS_DATA"]        = hoursJson,
                ["CLIENT_MEDIA_DATA"] = clientMediaJson,

                ["TOP_SERIES_ROWS"]      = BuildTopSeriesRows(topSeries),
                ["TOP_ARTISTES_ROWS"]    = BuildTopArtistesRows(topArtistes),
                ["TOP_CLIENTS_ROWS"]     = BuildTopClientsRows(topClients),
                ["TOP_MEDIA_PER_CLIENT"] = mediaStatsHtml
            };

            return ViewRenderer.Render("Backup.html", model);
        }

        // ---------------------------------------------------------------
        //  Filtres
        // ---------------------------------------------------------------
        private static List<MediaUsageItem> ApplyFilters(List<MediaUsageItem> all, BackupRequest req)
        {
            var items = all.ToList();

            items = req.Type switch
            {
                "audio" => items.Where(i => i.MediaType.Equals("Audio", StringComparison.OrdinalIgnoreCase)).ToList(),
                "serie" => items.Where(i => i.MediaType.Equals("Serie", StringComparison.OrdinalIgnoreCase)).ToList(),
                "video" => items.Where(i => i.MediaType.Equals("Video", StringComparison.OrdinalIgnoreCase)).ToList(),
                "rec"   => items.Where(i => i.MediaType.Equals("REC",   StringComparison.OrdinalIgnoreCase)).ToList(),
                "tv"    => items.Where(i => i.MediaType.Equals("TV",    StringComparison.OrdinalIgnoreCase)).ToList(),
                _       => items
            };

            if (req.Client != "all")
            {
                items = items.Where(i => i.ClientDisplay != null &&
                                         i.ClientDisplay.Equals(req.Client, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            DateTime now = DateTime.Now;
            items = req.Date switch
            {
                "today"     => items.Where(i => i.Timestamp.Date == now.Date).ToList(),
                "yesterday" => items.Where(i => i.Timestamp.Date == now.AddDays(-1).Date).ToList(),
                "7"         => items.Where(i => i.Timestamp >= now.AddDays(-7)).ToList(),
                "30"        => items.Where(i => i.Timestamp >= now.AddDays(-30)).ToList(),
                _           => items
            };

            items = req.Sort switch
            {
                "name_asc"  => items.OrderBy(i => i.Nom).ToList(),
                "name_desc" => items.OrderByDescending(i => i.Nom).ToList(),
                "date_asc"  => items.OrderBy(i => i.Timestamp).ToList(),
                _           => items.OrderByDescending(i => i.Timestamp).ToList()
            };

            return items;
        }

        // ---------------------------------------------------------------
        //  Construction du JSON des heures
        // ---------------------------------------------------------------
        private static string BuildHoursJson(List<MediaUsageItem> items)
        {
            var hoursAudio  = new int[24];
            var hoursSeries = new int[24];
            var hoursVideo  = new int[24];
            var hoursRec    = new int[24];
            var hoursTv     = new int[24];

            foreach (var it in items)
            {
                int h = it.Timestamp.Hour;
                switch ((it.MediaType ?? "").ToLowerInvariant())
                {
                    case "audio": hoursAudio[h]++;  break;
                    case "serie": hoursSeries[h]++; break;
                    case "video": hoursVideo[h]++;  break;
                    case "rec":   hoursRec[h]++;    break;
                    case "tv":    hoursTv[h]++;     break;
                }
            }

            var hoursData = new object[24];
            for (int h = 0; h < 24; h++)
            {
                hoursData[h] = new
                {
                    audio = hoursAudio[h],
                    serie = hoursSeries[h],
                    video = hoursVideo[h],
                    rec   = hoursRec[h],
                    tv    = hoursTv[h]
                };
            }
            return JsonSerializer.Serialize(hoursData);
        }

        private static string BuildClientOptions(List<string> allClients, string selected)
        {
            var sb = new StringBuilder();
            string allLabel = LanguageManager.Get("Tous") ?? "Tous";
            sb.Append($"<option value='all' {(selected == "all" ? "selected" : "")}>{allLabel}</option>");

            foreach (var c in allClients)
            {
                string val = c.ToLower();
                string sel = (val == selected) ? "selected" : "";
                sb.Append($"<option value='{WebUtility.HtmlEncode(val)}' {sel}>{WebUtility.HtmlEncode(c)}</option>");
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------
        //  Helpers stats avancées
        // ---------------------------------------------------------------
        private (string Serie, string Episode) ExtractSerie(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom)) return ("", "");
            var parts = nom.Split(" - ", 2, StringSplitOptions.TrimEntries);
            return parts.Length == 2 ? (parts[0], parts[1]) : (nom, "");
        }

        private (string Track, string Artiste, string Titre) ExtractAudio(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom)) return ("", "", "");
            var parts = nom.Split(" - ", StringSplitOptions.TrimEntries);
            if (parts.Length >= 3 && int.TryParse(parts[0], out _))
                return (parts[0], parts[1], string.Join(" - ", parts.Skip(2)));
            if (parts.Length >= 2)
                return ("", parts[0], string.Join(" - ", parts.Skip(1)));
            return ("", "", nom);
        }

        private List<(string Serie, int Count)> GetTopSeries(List<MediaUsageItem> items) =>
            items.Where(i => (i.MediaType ?? "").Equals("serie", StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(i.Nom))
                 .Select(i => ExtractSerie(i.Nom).Serie)
                 .Where(s => !string.IsNullOrWhiteSpace(s))
                 .GroupBy(s => s)
                 .Select(g => (Serie: g.Key, Count: g.Count()))
                 .OrderByDescending(x => x.Count)
                 .Take(10).ToList();

        private List<(string Artiste, int Count)> GetTopArtistes(List<MediaUsageItem> items) =>
            items.Where(i => (i.MediaType ?? "").Equals("audio", StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(i.Nom))
                 .Select(i => ExtractAudio(i.Nom).Artiste)
                 .Where(a => !string.IsNullOrWhiteSpace(a))
                 .GroupBy(a => a)
                 .Select(g => (Artiste: g.Key, Count: g.Count()))
                 .OrderByDescending(x => x.Count)
                 .Take(10).ToList();

        private List<(string Client, int Count)> GetTopClientsStats(List<MediaUsageItem> items) =>
            items.Where(i => !string.IsNullOrWhiteSpace(i.ClientDisplay))
                 .GroupBy(i => i.ClientDisplay)
                 .Select(g => (Client: g.Key, Count: g.Count()))
                 .OrderByDescending(x => x.Count)
                 .Take(10).ToList();

        private Dictionary<string, (int Audio, int Serie, int Video, int Rec, int Tv)>
            GetMediaStatsPerClient(List<MediaUsageItem> items) =>
            items.Where(i => !string.IsNullOrWhiteSpace(i.ClientDisplay)
                             && !string.IsNullOrWhiteSpace(i.MediaType))
                 .GroupBy(i => i.ClientDisplay)
                 .ToDictionary(
                     g => g.Key,
                     g => (
                         Audio: g.Count(x => x.MediaType.Equals("audio", StringComparison.OrdinalIgnoreCase)),
                         Serie: g.Count(x => x.MediaType.Equals("serie", StringComparison.OrdinalIgnoreCase)),
                         Video: g.Count(x => x.MediaType.Equals("video", StringComparison.OrdinalIgnoreCase)),
                         Rec:   g.Count(x => x.MediaType.Equals("rec",   StringComparison.OrdinalIgnoreCase)),
                         Tv:    g.Count(x => x.MediaType.Equals("tv",    StringComparison.OrdinalIgnoreCase))
                     ));

        private string BuildTopSeriesRows(List<(string Serie, int Count)> list)
        {
            var sb = new StringBuilder();
            foreach (var x in list)
                sb.Append($"<tr><td>{WebUtility.HtmlEncode(x.Serie)}</td><td>{x.Count}</td></tr>");
            return sb.ToString();
        }

        private string BuildTopArtistesRows(List<(string Artiste, int Count)> list)
        {
            var sb = new StringBuilder();
            foreach (var x in list)
                sb.Append($"<tr><td>{WebUtility.HtmlEncode(x.Artiste)}</td><td>{x.Count}</td></tr>");
            return sb.ToString();
        }

        private string BuildTopClientsRows(List<(string Client, int Count)> list)
        {
            var sb = new StringBuilder();
            foreach (var x in list)
                sb.Append($"<tr><td>{WebUtility.HtmlEncode(x.Client)}</td><td>{x.Count}</td></tr>");
            return sb.ToString();
        }

        private string BuildMediaStatsPerClientHtml(
            Dictionary<string, (int Audio, int Serie, int Video, int Rec, int Tv)> dict)
        {
            var sb = new StringBuilder();

            foreach (var kv in dict)
            {
                string client = kv.Key;
                var stats = kv.Value;
                bool isTuner = client.StartsWith("DVB-T", StringComparison.OrdinalIgnoreCase);

                var model = new Dictionary<string, object?>
                {
                    ["ClientName"] = WebUtility.HtmlEncode(client),
                    ["IfIsTuner"]  = isTuner,
                    ["IfNotTuner"] = !isTuner,
                    ["Audio"]      = stats.Audio,
                    ["Serie"]      = stats.Serie,
                    ["Video"]      = stats.Video,
                    ["Rec"]        = stats.Rec,
                    ["Tv"]         = stats.Tv
                };

                sb.Append(ViewRenderer.RenderPartial("ClientStats.html", model));
            }

            return sb.ToString();
        }

        private string RenderNoBackupMessage(string title)
        {
            var model = new Dictionary<string, object?> { ["TITLE"] = title };
            return ViewRenderer.Render("NoBackup.html", model);
        }
    }

    public class BackupRequest
    {
        public string Type   { get; set; } = "all";
        public string Client { get; set; } = "all";
        public string Date   { get; set; } = "all";
        public string Sort   { get; set; } = "date_desc";

        public static BackupRequest FromHttp(System.Net.HttpListenerRequest req)
        {
            return new BackupRequest
            {
                Type   = req.QueryString["type"]?.ToLower()   ?? "all",
                Client = req.QueryString["client"]?.ToLower() ?? "all",
                Date   = req.QueryString["date"]?.ToLower()   ?? "all",
                Sort   = req.QueryString["sort"]?.ToLower()   ?? "date_desc"
            };
        }
    }
}