using System;
using System.IO;
using System.Net;
using System.Text;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web.Templates;

namespace MediaMonitor.Service.Web.Rendering
{
    public static class FileRenderer
    {
        public static void Render(HttpListenerContext ctx, MediaMonitorEngine engine, string filePath)
        {
            // Analyse du fichier
            var info = FileAnalyzer.Analyze(filePath);

            // Détermination de l’icône selon le type de média
            string iconBasePath = Path.Combine(AppContext.BaseDirectory, "Resources", "Icons");
            string iconFile = info.MediaType switch
            {
                "Audio" => "webicon_audio.png",
                "Video" => "webicon_video.png",
                "Image" => "webicon_image.png",
                _ => "icon_file.png"
            };

            string iconFullPath = Path.Combine(iconBasePath, iconFile);
            byte[] iconBytes = File.ReadAllBytes(iconFullPath);
            string iconBase64 = "data:image/png;base64," + Convert.ToBase64String(iconBytes);

            // Charger le template
            string templatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "InfoPage.html");
            string html = File.ReadAllText(templatePath, Encoding.UTF8);

            // Champs simples
            html = html.Replace("{{IconPath}}", iconBase64);
            html = html.Replace("{{FileName}}", WebUtility.HtmlEncode(info.FileName));
            html = html.Replace("{{Title}}", WebUtility.HtmlEncode(info.Title ?? info.FileName));
            html = html.Replace("{{Path}}", WebUtility.HtmlEncode(info.Path));
            html = html.Replace("{{MediaType}}", WebUtility.HtmlEncode(info.MediaType));

            // Taille
            var fi = new FileInfo(info.Path);
            html = html.Replace("{{SizeMB}}", (fi.Length / 1024.0 / 1024.0).ToString("F2"));

            // Durée
            string durationText = info.Duration > 0
                ? TimeSpan.FromSeconds(info.Duration).ToString(@"hh\:mm\:ss")
                : "—";
            html = html.Replace("{{DurationText}}", durationText);

            // Miniature (image par défaut si rien)
            string coverBase64;
            if (info.AlbumArt != null && info.AlbumArt.Length > 0)
            {
                coverBase64 = "data:image/jpeg;base64," + Convert.ToBase64String(info.AlbumArt);
            }
            else
            {
                string defaultCoverPath = Path.Combine(AppContext.BaseDirectory, "Templates", "default-cover.png");
                byte[] defaultBytes = File.ReadAllBytes(defaultCoverPath);
                coverBase64 = "data:image/png;base64," + Convert.ToBase64String(defaultBytes);
            }
            html = html.Replace("{{AlbumArtBase64}}", coverBase64);

            // Vidéo
            html = html.Replace("{{SeriesName}}", WebUtility.HtmlEncode(info.SeriesName ?? ""));
            html = html.Replace("{{EpisodeName}}", WebUtility.HtmlEncode(info.EpisodeName ?? ""));
            html = html.Replace("{{Saison}}", info.Saison.ToString());
            html = html.Replace("{{Episode}}", info.Episode.ToString());
            html = html.Replace("{{VideoCodec}}", WebUtility.HtmlEncode(info.VideoCodec ?? ""));
            html = html.Replace("{{AudioCodec}}", WebUtility.HtmlEncode(info.AudioCodec ?? ""));

            // Audio
            html = html.Replace("{{TitleTag}}", WebUtility.HtmlEncode(info.Title ?? ""));
            html = html.Replace("{{Artist}}", WebUtility.HtmlEncode(info.Artist ?? ""));
            html = html.Replace("{{Album}}", WebUtility.HtmlEncode(info.Album ?? ""));
            html = html.Replace("{{Year}}", info.Year > 0 ? info.Year.ToString() : "—");
            html = html.Replace("{{Track}}", info.Track > 0 ? info.Track.ToString() : "—");
            html = html.Replace("{{Genre}}", WebUtility.HtmlEncode(info.Genre ?? ""));

            // Blocs conditionnels Mustache
            html = TemplateEngine.ApplyConditional(html, "IfDuration", info.Duration > 0);
            html = TemplateEngine.ApplyConditional(html, "IfVideo", info.MediaType == "Video");
            html = TemplateEngine.ApplyConditional(html, "IfAudio", info.MediaType == "Audio");
            html = TemplateEngine.ApplyConditional(html, "IfSeries", !string.IsNullOrEmpty(info.SeriesName));
            html = TemplateEngine.ApplyConditional(html, "IfMovie", info.MediaType == "Video" && string.IsNullOrEmpty(info.SeriesName));
            html = TemplateEngine.ApplyConditional(html, "IfSeasonEpisode", info.Saison > 0 || info.Episode > 0);
            html = TemplateEngine.ApplyConditional(html, "IfEpisodeName", !string.IsNullOrEmpty(info.EpisodeName));
            html = TemplateEngine.ApplyConditional(html, "IfVideoCodec", !string.IsNullOrEmpty(info.VideoCodec));
            html = TemplateEngine.ApplyConditional(html, "IfAudioCodec", !string.IsNullOrEmpty(info.AudioCodec));
            html = TemplateEngine.ApplyConditional(html, "IfRec", false);
            html = TemplateEngine.ApplyConditional(html, "IfFile", true);

            // Traduction
            html = TemplateEngine.Translate(html);

            // Envoi
            WebServer.SendHtml(ctx, html);
        }
    }
}
