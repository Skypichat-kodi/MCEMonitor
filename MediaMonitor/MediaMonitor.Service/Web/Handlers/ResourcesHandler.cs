using System;
using System.IO;
using System.Net;

namespace MediaMonitor.Service.Web.Handlers
{
    public static class ResourcesHandler
    {
        public static void Handle(HttpListenerContext ctx)
        {
            string url = ctx.Request.RawUrl ?? "";
            string baseDir = AppContext.BaseDirectory;

            // Exemple : /resources/icons/webicon_audio.png
            if (url.StartsWith("/resources/", StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = url.Substring("/resources/".Length)
                                         .Replace("/", Path.DirectorySeparatorChar.ToString());

                string fullPath = Path.Combine(baseDir, "Resources", relativePath);

                if (!File.Exists(fullPath))
                {
                    SendNotFound(ctx);
                    return;
                }

                SendFile(ctx, fullPath);
                return;
            }

            SendNotFound(ctx);
        }

        private static void SendFile(HttpListenerContext ctx, string fullPath)
        {
            try
            {
                byte[] data = File.ReadAllBytes(fullPath);

                string ext = Path.GetExtension(fullPath).ToLowerInvariant();
                string contentType = ext switch
                {
                    ".png" => "image/png",
                    ".jpg" => "image/jpeg",
                    ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".svg" => "image/svg+xml",
                    ".ico" => "image/x-icon",
                    ".json" => "application/json",
                    ".txt" => "text/plain",
                    _ => "application/octet-stream"
                };

                ctx.Response.ContentType = contentType;
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.OutputStream.Write(data, 0, data.Length);
                ctx.Response.OutputStream.Close();
            }
            catch
            {
                SendNotFound(ctx);
            }
        }

        private static void SendNotFound(HttpListenerContext ctx)
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.ContentType = "text/plain";
            using var writer = new StreamWriter(ctx.Response.OutputStream);
            writer.Write("404 - Not Found");
        }
    }
}
