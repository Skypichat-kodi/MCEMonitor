using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace NetMonitor.Service.Web
{
    /// <summary>
    /// Génération, envoi et validation des codes OTP à 6 chiffres.
    ///
    /// Un code est :
    ///   - généré via RandomNumberGenerator (pas Random)
    ///   - valable 5 minutes
    ///   - single-use (supprimé après validation réussie)
    ///   - limité à 3 tentatives
    ///   - jamais logué en clair
    ///
    /// Le code est stocké en mémoire uniquement (perdu au redémarrage,
    /// ce qui est acceptable : l'utilisateur refait la demande).
    /// </summary>
    public static class OtpService
    {
        private const int CodeExpiryMinutes = 5;
        private const int MaxAttempts       = 3;

        private sealed class Entry
        {
            public string Code { get; init; } = "";
            public DateTime ExpiresAt { get; init; }
            public int Attempts { get; set; }
        }

        private static readonly ConcurrentDictionary<string, Entry> _codes = new();

        public enum Result
        {
            Ok,
            Wrong,
            Expired,
            TooManyAttempts,
            NotFound
        }

        // ============================================================
        //  Génération + envoi du code
        // ============================================================
        /// <summary>
        /// Génère un code OTP, l'envoie par SMTP à l'adresse cfg.To,
        /// et retourne un token opaque pour la vérification.
        /// </summary>
        public static async Task<(string Token, bool Sent)> GenerateAndSendAsync(
            int codeLength,
            EmailConfig cfg)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.To))
                return ("", false);

            string code  = GenerateCode(codeLength);
            string token = Guid.NewGuid().ToString("N");

            _codes[token] = new Entry
            {
                Code      = code,
                ExpiresAt = DateTime.UtcNow.AddMinutes(CodeExpiryMinutes),
                Attempts  = 0
            };

            PurgeExpired();

            try
            {
                string subject = $"[NetMonitor] Code de vérification : {code}";
                string body =
                    $"Votre code de vérification NetMonitor est :\n\n" +
                    $"        {code}\n\n" +
                    $"Ce code est valable {CodeExpiryMinutes} minutes et ne peut être utilisé qu'une seule fois.\n\n" +
                    $"Si vous n'êtes pas à l'origine de cette demande, ignorez cet email.\n\n" +
                    $"— NetMonitor ({Environment.MachineName})";

                await EmailSender.SendAsync(cfg, subject, body, isHtml: false);
                CoreLog.Write($"[OTP] Code envoyé à {cfg.To}");
                return (token, true);
            }
            catch (Exception ex)
            {
                _codes.TryRemove(token, out _);
                CoreLog.Error($"[OTP] Échec envoi email : {ex.Message}");
                return ("", false);
            }
        }

        // ============================================================
        //  Validation
        // ============================================================
        public static Result Validate(string? token, string? userCode)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userCode))
                return Result.NotFound;

            if (!_codes.TryGetValue(token, out var entry))
                return Result.NotFound;

            if (entry.ExpiresAt < DateTime.UtcNow)
            {
                _codes.TryRemove(token, out _);
                return Result.Expired;
            }

            entry.Attempts++;
            if (entry.Attempts > MaxAttempts)
            {
                _codes.TryRemove(token, out _);
                return Result.TooManyAttempts;
            }

            // Comparaison à temps constant
            byte[] a = System.Text.Encoding.UTF8.GetBytes(entry.Code);
            byte[] b = System.Text.Encoding.UTF8.GetBytes(userCode.Trim());

            bool ok = a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);

            if (!ok) return Result.Wrong;

            _codes.TryRemove(token, out _);
            return Result.Ok;
        }

        // ============================================================
        //  Helpers
        // ============================================================
        private static string GenerateCode(int length)
        {
            if (length < 4) length = 4;
            if (length > 10) length = 10;

            var digits = new char[length];
            for (int i = 0; i < length; i++)
                digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
            return new string(digits);
        }

        private static void PurgeExpired()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _codes)
            {
                if (kv.Value.ExpiresAt < now)
                    _codes.TryRemove(kv.Key, out _);
            }
        }

        // ============================================================
        //  Fallback LAN — si SMTP down, on autorise localhost + IP LAN
        // ============================================================
        /// <summary>
        /// Retourne true si la requête vient de localhost ou d'une IP
        /// de la machine (mode de secours quand SMTP est indisponible).
        /// </summary>
        public static bool IsLocalRequest(string? remoteIp, System.Collections.Generic.IEnumerable<string> localIps)
        {
            if (string.IsNullOrWhiteSpace(remoteIp)) return false;

            if (remoteIp == "127.0.0.1" || remoteIp == "::1") return true;

            foreach (var ip in localIps)
                if (string.Equals(ip, remoteIp, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }
    }
}