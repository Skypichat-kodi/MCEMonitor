using System.Security.Cryptography;
using System.Text;

namespace MediaMonitor.Service.Web
{
    public static class LoginService
    {
        public static bool CheckCredentials(string? username, string? password, WebServerSettings settings)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return false;

            byte[] a = Encoding.UTF8.GetBytes($"{username}:{password}");
            byte[] b = Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}");

            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }
    }
}