using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NetMonitor.Service.Web
{
    /// <summary>
    /// Gère le login unifié :
    ///   - Vérifie user/password contre NetMonitor.web.config
    ///   - Si IP locale : session créée directement (bypass OTP)
    ///   - Si IP distante + OtpEnabled : envoie OTP, valide le code, crée la session
    ///
    /// Les codes OTP en attente sont stockés en mémoire (ConcurrentDictionary),
    /// expiration 5 min, single-use, 3 tentatives max.
    /// </summary>
    public static class LoginService
    {
        private const int OtpExpiryMinutes = 5;
        private const int OtpMaxAttempts   = 3;

        private sealed class PendingOtp
        {
            public string Code { get; init; } = "";
            public DateTime ExpiresAt { get; init; }
            public int Attempts { get; set; }
        }

        // Clé = IP source (pour éviter qu'un attaquant génère 1000 codes)
        private static readonly ConcurrentDictionary<string, PendingOtp> _pendingOtps = new();

        private static readonly object _otpLock = new();

        public enum LoginResult
        {
            Ok,
            BadCredentials,
            OtpRequired,      // user/password OK, OTP envoyé, formulaire à réafficher
            BadOtp,
            OtpExpired,
            TooManyAttempts,
            SmtpError
        }

        // ============================================================
        //  Vérification user/password
        // ============================================================
        public static bool CheckCredentials(string? username, string? password, NetMonitorSettings settings)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return false;

            // Comparaison à temps constant
            byte[] a = Encoding.UTF8.GetBytes($"{username}:{password}");
            byte[] b = Encoding.UTF8.GetBytes($"{settings.WebUsername}:{settings.WebPassword}");

            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }

        // ============================================================
        //  Envoi de l'OTP (si nécessaire)
        // ============================================================
        public static async Task<(LoginResult Result, bool OtpSent)> SendOtpIfNeededAsync(
            string remoteIp,
            NetMonitorSettings settings)
        {
            if (!settings.OtpEnabled)
                return (LoginResult.Ok, false);

            // IP locale → bypass
            // (la liste _localIps est passée en paramètre au MiniHttpServer,
            //  on fait donc confiance à l'appelant pour avoir vérifié en amont)

            var cfg = EmailConfig.Load();
            CoreLog.Write($"[LOGIN] SendOtpIfNeeded : Server={(string.IsNullOrWhiteSpace(cfg.Server) ? "VIDE" : cfg.Server)}, To={(string.IsNullOrWhiteSpace(cfg.To) ? "VIDE" : cfg.To)}");

            if (string.IsNullOrWhiteSpace(cfg.Server) || string.IsNullOrWhiteSpace(cfg.To))
            {
                CoreLog.Write("[LOGIN] SendOtpIfNeeded : config SMTP incomplète, abandon");
                return (LoginResult.SmtpError, false);
            }

            string code = GenerateCode(settings.OtpCodeLength);

            _pendingOtps[remoteIp] = new PendingOtp
            {
                Code = code,
                ExpiresAt = DateTime.UtcNow.AddMinutes(OtpExpiryMinutes),
                Attempts = 0
            };

            try
            {
                string subject = $"[NetMonitor] Code de connexion : {code}";
                string body =
                    $"Votre code de connexion NetMonitor est :\n\n" +
                    $"        {code}\n\n" +
                    $"Ce code est valable {OtpExpiryMinutes} minutes et ne peut être utilisé qu'une seule fois.\n\n" +
                    $"Si vous n'êtes pas à l'origine de cette demande, ignorez cet email.\n\n" +
                    $"— NetMonitor ({Environment.MachineName})";

                await EmailSender.SendAsync(cfg, subject, body, isHtml: false);
                CoreLog.Write($"[LOGIN] OTP envoyé à {cfg.To} (IP {remoteIp})");
                return (LoginResult.OtpRequired, true);
            }
            catch (Exception ex)
            {
                _pendingOtps.TryRemove(remoteIp, out _);
                CoreLog.Error($"[LOGIN] Échec envoi OTP : {ex.Message}");
                return (LoginResult.SmtpError, false);
            }
        }

        // ============================================================
        //  Validation de l'OTP
        // ============================================================
        public static LoginResult ValidateOtp(string remoteIp, string? userCode)
        {
            if (string.IsNullOrWhiteSpace(userCode))
                return LoginResult.BadOtp;

            lock (_otpLock)
            {
                if (!_pendingOtps.TryGetValue(remoteIp, out var pending))
                    return LoginResult.OtpExpired;

                if (pending.ExpiresAt < DateTime.UtcNow)
                {
                    _pendingOtps.TryRemove(remoteIp, out _);
                    return LoginResult.OtpExpired;
                }

                pending.Attempts++;
                if (pending.Attempts > OtpMaxAttempts)
                {
                    _pendingOtps.TryRemove(remoteIp, out _);
                    return LoginResult.TooManyAttempts;
                }

                byte[] a = Encoding.UTF8.GetBytes(pending.Code);
                byte[] b = Encoding.UTF8.GetBytes(userCode.Trim());

                bool ok = a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);

                if (!ok) return LoginResult.BadOtp;

                _pendingOtps.TryRemove(remoteIp, out _);
                return LoginResult.Ok;
            }
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

        /// <summary>Purge les OTP expirés (appelé périodiquement).</summary>
        public static void PurgeExpired()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _pendingOtps)
            {
                if (kv.Value.ExpiresAt < now)
                    _pendingOtps.TryRemove(kv.Key, out _);
            }
        }
    }
}