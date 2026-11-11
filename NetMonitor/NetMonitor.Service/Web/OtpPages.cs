using System;
using System.Net;
using System.Text;

namespace NetMonitor.Service.Web
{
    /// <summary>
    /// Pages HTML minimalistes pour /otp et /otp/verify.
    /// Volontairement sans JS ni CSS externe (rapide, cache-friendly).
    /// </summary>
    public static class OtpPages
    {
        public static void SendRequestPage(HttpListenerContext ctx, string token, string email, string? originalUrl)
        {
            string safeEmail    = WebUtility.HtmlEncode(email);
            string safeToken    = WebUtility.HtmlEncode(token);
            string safeOriginal = WebUtility.HtmlEncode(originalUrl ?? "/");

            string html = $@"<!DOCTYPE html>
<html lang=""fr""><head><meta charset=""utf-8"">
<title>Vérification OTP - NetMonitor</title>
<style>
  body {{ font-family: Segoe UI, Arial, sans-serif; background: #1E1E1E; color: #E5E5E5;
         display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }}
  .card {{ background: #252526; border: 1px solid #3C3C3C; border-radius: 8px;
           padding: 30px; max-width: 420px; box-shadow: 0 4px 12px rgba(0,0,0,.4); }}
  h1 {{ color: #4CC2FF; margin-top: 0; font-size: 20px; }}
  p  {{ line-height: 1.6; }}
  input[type=text] {{ width: 100%; padding: 10px; font-size: 24px; letter-spacing: 8px;
                      text-align: center; background: #1E1E1E; border: 1px solid #3C3C3C;
                      border-radius: 4px; color: #FFF; box-sizing: border-box; }}
  input[type=submit] {{ width: 100%; padding: 12px; margin-top: 15px; font-size: 15px;
                        background: #4CC2FF; border: none; border-radius: 4px;
                        color: #000; font-weight: bold; cursor: pointer; }}
  input[type=submit]:hover {{ background: #6AD3FF; }}
  .hint {{ color: #888; font-size: 12px; margin-top: 15px; }}
</style></head><body>
<div class=""card"">
  <h1>🔐 Vérification OTP</h1>
  <p>Un code à 6 chiffres a été envoyé à <b>{safeEmail}</b>.</p>
  <p>Il est valable 5 minutes.</p>
  <form method=""post"" action=""/otp/verify"">
    <input type=""hidden"" name=""token"" value=""{safeToken}"" />
    <input type=""hidden"" name=""original"" value=""{safeOriginal}"" />
    <input type=""text"" name=""code"" maxlength=""10"" autofocus pattern=""[0-9]*"" inputmode=""numeric"" required />
    <input type=""submit"" value=""Valider"" />
  </form>
  <p class=""hint"">Le code est à usage unique. 3 tentatives maximum.</p>
</div>
</body></html>";

            WriteHtml(ctx, html, 200);
        }

        public static void SendErrorPage(HttpListenerContext ctx, string message, string? retryUrl)
        {
            string safeMsg    = WebUtility.HtmlEncode(message);
            string safeRetry  = WebUtility.HtmlEncode(retryUrl ?? "/");

            string html = $@"<!DOCTYPE html>
<html lang=""fr""><head><meta charset=""utf-8"">
<title>Erreur OTP - NetMonitor</title>
<style>
  body {{ font-family: Segoe UI, Arial, sans-serif; background: #1E1E1E; color: #E5E5E5;
         display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }}
  .card {{ background: #252526; border: 1px solid #C0392B; border-radius: 8px;
           padding: 30px; max-width: 420px; }}
  h1 {{ color: #FF6347; margin-top: 0; font-size: 20px; }}
  a {{ color: #4CC2FF; }}
</style></head><body>
<div class=""card"">
  <h1>⚠ Vérification échouée</h1>
  <p>{safeMsg}</p>
  <p><a href=""{safeRetry}"">↩ Retour</a></p>
</div>
</body></html>";

            WriteHtml(ctx, html, 401);
        }

        private static void WriteHtml(HttpListenerContext ctx, string html, int status)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = buffer.Length;
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.OutputStream.Close();
        }
    }
}