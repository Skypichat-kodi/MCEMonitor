using System.Net;
using System.Text;

namespace NetMonitor.Service.Web
{
    /// <summary>
    /// Pages HTML pour le login unifié (user + password + OTP optionnel).
    /// </summary>
    public static class LoginPages
    {
        public static void SendLoginPage(HttpListenerContext ctx, int step, string? error, string? originalUrl = null)
        {
            // step 1 = credentials seuls, step 2 = credentials + OTP
            bool needsOtp = step >= 2;

            string errorHtml = string.IsNullOrEmpty(error)
                ? ""
                : $"<div class='error'>{WebUtility.HtmlEncode(error)}</div>";

            string otpField = needsOtp
                ? @"<label>Code OTP (6 chiffres)</label>
                   <input type='text' name='otp' maxlength='6' inputmode='numeric' pattern='[0-9]*' required />"
                : "";

            string original = WebUtility.HtmlEncode(originalUrl ?? "/");

            string html = $@"<!DOCTYPE html>
        <html lang=""fr""><head><meta charset=""utf-8"">
        <title>Connexion - NetMonitor</title>
        <style>
          body {{ font-family: Segoe UI, Arial, sans-serif; background: #1E1E1E; color: #E5E5E5;
                 display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }}
          .card {{ background: #252526; border: 1px solid #3C3C3C; border-radius: 8px;
                   padding: 30px; max-width: 400px; width: 100%; box-shadow: 0 4px 12px rgba(0,0,0,.4); }}
          h1 {{ color: #4CC2FF; margin-top: 0; font-size: 22px; text-align: center; }}
          .subtitle {{ color: #888; font-size: 12px; text-align: center; margin-bottom: 20px; }}
          label {{ display: block; margin: 15px 0 5px; font-size: 13px; color: #B4B4B4; }}
          input[type=text], input[type=password] {{
              width: 100%; padding: 10px; font-size: 14px; background: #1E1E1E;
              border: 1px solid #3C3C3C; border-radius: 4px; color: #FFF; box-sizing: border-box;
          }}
          input[type=text]:focus, input[type=password]:focus {{ outline: none; border-color: #4CC2FF; }}
          input[type=submit] {{
              width: 100%; padding: 12px; margin-top: 20px; font-size: 15px;
              background: #4CC2FF; border: none; border-radius: 4px;
              color: #000; font-weight: bold; cursor: pointer;
          }}
          input[type=submit]:hover {{ background: #6AD3FF; }}
          .error {{ background: #3A1A1A; border: 1px solid #FF6347; color: #FF6347;
                    padding: 10px; border-radius: 4px; margin-bottom: 15px; font-size: 13px; }}
          .hint {{ color: #888; font-size: 11px; margin-top: 15px; text-align: center; }}
        </style></head><body>
        <div class=""card"">
          <h1>🔐 NetMonitor</h1>
          <p class=""subtitle"">Machine : {WebUtility.HtmlEncode(System.Environment.MachineName)}</p>
          {errorHtml}
          <form method=""post"" action=""/login"">
            <input type=""hidden"" name=""step"" value=""{step}"" />
            <input type=""hidden"" name=""original"" value=""{original}"" />
            <label>Nom d'utilisateur</label>
            <input type=""text"" name=""username"" autofocus required />
            <label>Mot de passe</label>
            <input type=""password"" name=""password"" required />
            {otpField}
            <input type=""submit"" value=""Se connecter"" />
          </form>
          <p class=""hint"">{(
              needsOtp
                  ? "Un code OTP vous a été envoyé par email."
                  : "Accès sécurisé — un code vous sera envoyé après validation."
          )}</p>
        </div>
        </body></html>";

            WriteHtml(ctx, html, 200);
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