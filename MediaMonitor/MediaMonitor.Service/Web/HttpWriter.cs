using System;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MediaMonitor.Service.Web
{
    /// <summary>
    /// Petits helpers pour écrire des réponses HTTP (HTML, JSON, image).
    /// </summary>
    public static class HttpWriter
    {
        public static void WriteHtml(HttpListenerContext ctx, string html, int status = 200)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = buffer.Length;
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.OutputStream.Close();
        }

        public static void WriteJson(HttpListenerContext ctx, object data)
        {
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            byte[] buffer = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = buffer.Length;
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.OutputStream.Close();
        }

        public static void WriteBytes(HttpListenerContext ctx, byte[] bytes, string contentType)
        {
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        public static void Write404(HttpListenerContext ctx)
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }

        public static void Write401(HttpListenerContext ctx)
        {
            ctx.Response.StatusCode = 401;
            ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=\"MediaMonitor\"");
            ctx.Response.Close();
        }
    }
}