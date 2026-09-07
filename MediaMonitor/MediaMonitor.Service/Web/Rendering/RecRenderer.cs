using System;
using System.IO;
using System.Net;
using System.Text;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web.Templates;

namespace MediaMonitor.Service.Web.Rendering
{
    public static class RecRenderer
    {
        public static void Render(HttpListenerContext ctx, MediaMonitorEngine engine, string filePath)
        {
            // Analyse REC locale
            var rec = engine.FindRecByPath(filePath);
            if (rec == null)
                rec = FileAnalyzer.Analyze(filePath);

            // Charger template
            string recTemplatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "InfoPage.html");
            string recHtml = File.ReadAllText(recTemplatePath, Encoding.UTF8);

            // Icône REC
            string recIconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Icons", "webicon_serie.png");
            string recIconBase64 = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(recIconPath));
            recHtml = recHtml.Replace("{{IconPath}}", recIconBase64);

            // Champs REC
            recHtml = recHtml.Replace("{{FileName}}", WebUtility.HtmlEncode(rec.Nom));
            recHtml = recHtml.Replace("{{Title}}", WebUtility.HtmlEncode(rec.Nom));
            recHtml = recHtml.Replace("{{Channel}}", WebUtility.HtmlEncode(rec.Channel));
            recHtml = recHtml.Replace("{{Path}}", WebUtility.HtmlEncode(rec.Path));
            recHtml = recHtml.Replace("{{MediaType}}", "REC");

            // Durée REC
            string recDurationText = rec.Duration > 0
                ? TimeSpan.FromSeconds(rec.Duration).ToString(@"hh\:mm\:ss")
                : "—";
            recHtml = recHtml.Replace("{{RecDuration}}", recDurationText);

            // Champs vidéo/série
            recHtml = recHtml.Replace("{{SeriesName}}", WebUtility.HtmlEncode(rec.SeriesName ?? ""));
            recHtml = recHtml.Replace("{{EpisodeName}}", WebUtility.HtmlEncode(rec.EpisodeName ?? ""));
            recHtml = recHtml.Replace("{{Saison}}", rec.Saison.ToString());
            recHtml = recHtml.Replace("{{Episode}}", rec.Episode.ToString());

            // Blocs conditionnels
            recHtml = TemplateEngine.ApplyConditional(recHtml, "IfRec", true);
            recHtml = TemplateEngine.ApplyConditional(recHtml, "IfFile", false);
            recHtml = TemplateEngine.ApplyConditional(recHtml, "IfVideo", false);
            recHtml = TemplateEngine.ApplyConditional(recHtml, "IfAudio", false);

            recHtml = TemplateEngine.ApplyConditional(recHtml, "IfSeries", !string.IsNullOrEmpty(rec.SeriesName));
            recHtml = TemplateEngine.ApplyConditional(recHtml, "IfMovie", string.IsNullOrEmpty(rec.SeriesName));
            recHtml = TemplateEngine.ApplyConditional(recHtml, "IfSeasonEpisode", rec.Saison > 0 || rec.Episode > 0);
            recHtml = TemplateEngine.ApplyConditional(recHtml, "IfEpisodeName", !string.IsNullOrEmpty(rec.EpisodeName));

            recHtml = TemplateEngine.ApplyConditional(recHtml, "IfDuration", rec.Duration > 0);
            recHtml = TemplateEngine.ApplyConditional(recHtml, "IfVideoCodec", false);
            recHtml = TemplateEngine.ApplyConditional(recHtml, "IfAudioCodec", false);

            // Traduction
            recHtml = TemplateEngine.Translate(recHtml);

            // Envoi
            WebServer.SendHtml(ctx, recHtml);
        }
    }
}
