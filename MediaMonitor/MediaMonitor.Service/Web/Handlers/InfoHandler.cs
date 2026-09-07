using System;
using System.Net;
using System.IO;
using System.Text;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web.Rendering;
using MediaMonitor.Service.Web.Templates;

namespace MediaMonitor.Service.Web.Handlers
{
    public static class InfoHandler
    {
        public static void Handle(HttpListenerContext ctx, MediaMonitorEngine engine)
        {
            ctx.Response.ContentEncoding = Encoding.UTF8;
            ctx.Response.ContentType = "text/html; charset=utf-8";

            // Lire le paramètre "path"
            string filePath = ctx.Request.QueryString["path"];
            filePath = WebUtility.UrlDecode(filePath);

            if (string.IsNullOrWhiteSpace(filePath))
            {
                WebServer.SendHtml(ctx,
                    "<html><body><h2>Chemin invalide.</h2></body></html>");
                return;
            }

            // REC = pas d’extension
            if (IsRec(filePath))
            {
                RecRenderer.Render(ctx, engine, filePath);
                return;
            }

            // Fichier normal
            FileRenderer.Render(ctx, engine, filePath);
        }

        private static bool IsRec(string filePath)
        {
            return string.IsNullOrEmpty(Path.GetExtension(filePath));
        }
    }
}
