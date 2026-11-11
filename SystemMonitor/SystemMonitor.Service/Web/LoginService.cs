using System.Security.Cryptography;
using System.Text;

namespace SystemMonitor.Service.Web
{
    /// <summary>
    /// Vérifie user/password contre SystemMonitor.web.config.
    /// Pas d'OTP pour SystemMonitor (lecture seule).
    /// </summary>
    public static class LoginService
    {
        public static bool CheckCredentials(string? username, string? password, SystemMonitorSettings settings)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return false;

            // Comparaison à temps constant
            byte[] a = Encoding.UTF8.GetBytes($"{username}:{password}");
            byte[] b = Encoding.UTF8.GetBytes($"{settings.WebUsername}:{settings.WebPassword}");

            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }
    }
}