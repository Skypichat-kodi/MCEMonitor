using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web;

namespace MediaMonitor.Service.Web.Handlers
{
    public class LogoHandler
    {
        public async Task ServeAsync(HandlerContext ctx)
        {
            try
            {
                string channel = ctx.Http.Request.QueryString["channel"];

                if (string.IsNullOrWhiteSpace(channel))
                {
                    ctx.Http.Response.StatusCode = 400;
                    ctx.Http.Response.Close();
                    return;
                }

                string encoded = Uri.EscapeDataString(channel);

                string baseUrl = ctx.Settings.DvbViewerUrl;
                baseUrl = baseUrl.Replace("/status.html?aktion=status", "");

                string logoUrl = $"{baseUrl}/Logos/{encoded}.png?height=200";

                var handler = new HttpClientHandler
                {
                    Credentials = new NetworkCredential(
                        ctx.Settings.DvbViewerUser,
                        ctx.Settings.DvbViewerPass)
                };

                using var http = new HttpClient(handler);
                byte[] bytes = await http.GetByteArrayAsync(logoUrl);

                ctx.Http.Response.ContentType = "image/png";
                ctx.Http.Response.ContentLength64 = bytes.Length;
                await ctx.Http.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                ctx.Http.Response.Close();
            }
            catch (Exception ex)
            {
                CoreLog.Write("LOGO ERROR: " + ex.Message);
                ctx.Http.Response.StatusCode = 404;
                ctx.Http.Response.Close();
            }
        }
    }
}