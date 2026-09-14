using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using MediaMonitor.Core.Language;
using MediaMonitor.Core.Services;

namespace MediaMonitor.Service.Web.Handlers
{
    public class InfoHandler
    {
        private const string IconsDir = "Resources/Icons";

        public void Show(HandlerContext ctx)
        {
            string filePath = WebUtility.UrlDecode(ctx.Http.Request.QueryString["path"]);

            if (string.IsNullOrWhiteSpace(filePath))
            {
                HttpWriter.WriteHtml(ctx.Http,
                    "<html><body><h2>Chemin invalide.</h2></body></html>");
                return;
            }

            var model = BuildModel(ctx.Engine, filePath);

            string html = ViewRenderer.Render("InfoPage.html", model);
            HttpWriter.WriteHtml(ctx.Http, html);
        }

        // ---------------------------------------------------------------
        //  Construction du modèle : 4 cas unifiés
        // ---------------------------------------------------------------
        private Dictionary<string, object?> BuildModel(MediaMonitorEngine engine, string filePath)
        {
            string ext = Path.GetExtension(filePath);

            // Cas 1 : REC (pas d'extension)
            if (string.IsNullOrEmpty(ext))
            {
                var rec = engine.FindRecByPath(filePath) ?? FileAnalyzer.Analyze(filePath);
                return ModelFromRec(rec);
            }

            // Cas 2 : Fichier réel existant
            if (File.Exists(filePath))
            {
                var info = FileAnalyzer.Analyze(filePath);
                return ModelFromFile(info);
            }

            // Cas 3 : Fichier disparu mais dans engine (live/history/backup)
            var item = engine.FindByPath(filePath);
            if (item != null)
                return ModelFromEngineItem(item);

            // Cas 4 : Fallback (analyse "à vide")
            var fallback = FileAnalyzer.Analyze(filePath);
            return ModelFromFile(fallback);
        }

        // ---------------------------------------------------------------
        //  Constructeurs de modèles (un par cas)
        // ---------------------------------------------------------------
        private Dictionary<string, object?> ModelFromRec(dynamic rec)
        {
            string recDurationText = rec.Duration > 0
                ? TimeSpan.FromSeconds(rec.Duration).ToString(@"hh\:mm\:ss")
                : "—";

            var m = new Dictionary<string, object?>
            {
                ["IconPath"]    = IconBase64("webicon_serie.png"),
                ["FileName"]    = WebUtility.HtmlEncode(rec.Nom),
                ["Title"]       = WebUtility.HtmlEncode(rec.Nom),
                ["Channel"]     = WebUtility.HtmlEncode(rec.Channel),
                ["ChannelLogo"] = WebUtility.HtmlEncode(rec.ChannelLogo ?? ""),
                ["Path"]        = WebUtility.HtmlEncode(rec.Path),
                ["MediaType"]   = "REC",
                ["RecDuration"] = recDurationText,
                ["SeriesName"]  = WebUtility.HtmlEncode(rec.SeriesName ?? ""),
                ["EpisodeName"] = WebUtility.HtmlEncode(rec.EpisodeName ?? ""),
                ["Saison"]      = rec.Saison.ToString(),
                ["Episode"]     = rec.Episode.ToString(),

                // Flags
                ["IfRec"]               = true,
                ["IfFile"]              = false,
                ["IfVideo"]             = false,
                ["IfAudio"]             = false,
                ["IfRecSerie"]          = !string.IsNullOrEmpty(rec.SeriesName),
                ["IfRecMovie"]          = string.IsNullOrEmpty(rec.SeriesName),
                ["IfRecEpisode"]        = !string.IsNullOrEmpty(rec.EpisodeName),
                ["IfRecSeasonEpisode"]  = rec.Saison > 0 || rec.Episode > 0,
                ["IfDuration"]          = rec.Duration > 0
            };

            // Champs à ne pas afficher dans ce cas : vides
            FillMissing(m, new[]
            {
                "IfSeries","IfMovie","IfSeasonEpisode","IfEpisodeName",
                "IfVideoCodec","IfAudioCodec"
            }, false);

            return m;
        }

        private Dictionary<string, object?> ModelFromFile(dynamic info)
        {
            string durationText = info.Duration > 0
                ? TimeSpan.FromSeconds(info.Duration).ToString(@"hh\:mm\:ss")
                : "—";

            string coverBase64;
            if (info.AlbumArt != null && info.AlbumArt.Length > 0)
                coverBase64 = "data:image/jpeg;base64," + Convert.ToBase64String(info.AlbumArt);
            else
                coverBase64 = DefaultCoverBase64();

            string iconFile = info.MediaType switch
            {
                "Audio" => "webicon_audio.png",
                "Video" => "webicon_video.png",
                "Image" => "webicon_image.png",
                _       => "icon_file.png"
            };

            var fi = new FileInfo(info.Path);

            var m = new Dictionary<string, object?>
            {
                ["IconPath"]        = IconBase64(iconFile),
                ["FileName"]        = WebUtility.HtmlEncode(info.FileName),
                ["Title"]           = WebUtility.HtmlEncode(info.Title ?? info.FileName),
                ["Path"]            = WebUtility.HtmlEncode(info.Path),
                ["MediaType"]       = WebUtility.HtmlEncode(info.MediaType),
                ["SizeMB"]          = (fi.Length / 1024.0 / 1024.0).ToString("F2"),
                ["DurationText"]    = durationText,
                ["AlbumArtBase64"]  = coverBase64,

                ["SeriesName"]      = WebUtility.HtmlEncode(info.SeriesName ?? ""),
                ["EpisodeName"]     = WebUtility.HtmlEncode(info.EpisodeName ?? ""),
                ["Saison"]          = info.Saison.ToString(),
                ["Episode"]         = info.Episode.ToString(),
                ["VideoCodec"]      = WebUtility.HtmlEncode(info.VideoCodec ?? ""),
                ["AudioCodec"]      = WebUtility.HtmlEncode(info.AudioCodec ?? ""),

                ["TitleTag"]        = WebUtility.HtmlEncode(info.Title ?? ""),
                ["Artist"]          = WebUtility.HtmlEncode(info.Artist ?? ""),
                ["Album"]           = WebUtility.HtmlEncode(info.Album ?? ""),
                ["Year"]            = info.Year > 0 ? info.Year.ToString() : "—",
                ["Track"]           = info.Track > 0 ? info.Track.ToString() : "—",
                ["Genre"]           = WebUtility.HtmlEncode(info.Genre ?? ""),

                // Flags
                ["IfDuration"]      = info.Duration > 0,
                ["IfVideo"]         = info.MediaType == "Video",
                ["IfAudio"]         = info.MediaType == "Audio",
                ["IfSeries"]        = !string.IsNullOrEmpty(info.SeriesName),
                ["IfMovie"]         = info.MediaType == "Video" && string.IsNullOrEmpty(info.SeriesName),
                ["IfSeasonEpisode"] = info.Saison > 0 || info.Episode > 0,
                ["IfEpisodeName"]   = !string.IsNullOrEmpty(info.EpisodeName),
                ["IfVideoCodec"]    = !string.IsNullOrEmpty(info.VideoCodec),
                ["IfAudioCodec"]    = !string.IsNullOrEmpty(info.AudioCodec),
                ["IfRec"]           = false,
                ["IfFile"]          = true
            };

            // REC-only flags
            FillMissing(m, new[]
            {
                "IfRecSerie","IfRecMovie","IfRecEpisode","IfRecSeasonEpisode",
                "IfChannel","IfRecDuration"
            }, false);

            return m;
        }

        private Dictionary<string, object?> ModelFromEngineItem(MediaUsageItem item)
        {
            string iconFile = (item.MediaType ?? "").ToLowerInvariant() switch
            {
                "audio" => "webicon_audio.png",
                "video" => "webicon_video.png",
                "rec"   => "webicon_serie.png",
                "tv"    => "webicon_tv.png",
                _       => "icon_file.png"
            };

            var m = new Dictionary<string, object?>
            {
                ["IconPath"]    = IconBase64(iconFile),
                ["FileName"]    = WebUtility.HtmlEncode(item.FileName),
                ["Title"]       = WebUtility.HtmlEncode(item.Nom),
                ["Path"]        = WebUtility.HtmlEncode(item.Path),
                ["MediaType"]   = WebUtility.HtmlEncode(item.MediaType),
                ["SeriesName"]  = WebUtility.HtmlEncode(item.SeriesName ?? ""),
                ["EpisodeName"] = WebUtility.HtmlEncode(item.EpisodeName ?? ""),
                ["Saison"]      = item.Saison.ToString(),
                ["Episode"]     = item.Episode.ToString(),
                ["Channel"]     = WebUtility.HtmlEncode(item.Channel ?? ""),
                ["ChannelLogo"] = WebUtility.HtmlEncode(item.ChannelLogo ?? ""),

                ["IfRec"]           = item.MediaType.Equals("rec", StringComparison.OrdinalIgnoreCase),
                ["IfFile"]          = false,
                ["IfVideo"]         = item.MediaType.Equals("video", StringComparison.OrdinalIgnoreCase),
                ["IfAudio"]         = item.MediaType.Equals("audio", StringComparison.OrdinalIgnoreCase),
                ["IfSeries"]        = !string.IsNullOrEmpty(item.SeriesName),
                ["IfMovie"]         = item.MediaType.Equals("video", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(item.SeriesName),
                ["IfSeasonEpisode"] = item.Saison > 0 || item.Episode > 0,
                ["IfEpisodeName"]   = !string.IsNullOrEmpty(item.EpisodeName)
            };

            // Flags vides (non applicables)
            FillMissing(m, new[]
            {
                "IfDuration","IfVideoCodec","IfAudioCodec",
                "IfRecSerie","IfRecMovie","IfRecEpisode","IfRecSeasonEpisode",
                "IfChannel","IfRecDuration"
            }, false);

            return m;
        }

        // ---------------------------------------------------------------
        //  Helpers
        // ---------------------------------------------------------------
        private static string IconBase64(string iconFile)
        {
            string path = Path.Combine(AppContext.BaseDirectory, IconsDir, iconFile);
            return "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path));
        }

        private static string DefaultCoverBase64()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Resources", "Images", "default-cover.png");
            return "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path));
        }

        private static void FillMissing(Dictionary<string, object?> m, string[] keys, object? value)
        {
            foreach (var k in keys)
            {
                if (!m.ContainsKey(k))
                    m[k] = value;
            }
        }
    }
}