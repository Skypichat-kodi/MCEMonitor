using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace MCEMonitor.Utils
{
    public enum SmtpStatus
    {
        NotConfigured,     // Champs manquants dans email.config
        Unreachable,       // Serveur/port injoignable
        AuthFailed,        // Login/mot de passe refusés
        Ready,             // Tout est bon
        Unknown            // Erreur inattendue
    }

    public static class SmtpHealthChecker
    {
        private static SmtpStatus _lastStatus = SmtpStatus.Unknown;
        private static DateTime _lastCheck = DateTime.MinValue;
        private static readonly object _lock = new();

        public static SmtpStatus LastStatus => _lastStatus;
        public static DateTime LastCheck => _lastCheck;

        /// <summary>
        /// Vérifie la config SMTP. Cache le résultat pendant N minutes (0 = pas de cache).
        /// </summary>
        public static async Task<SmtpStatus> CheckAsync(int cacheMinutes = 10)
        {
            lock (_lock)
            {
                if (cacheMinutes > 0 &&
                    (DateTime.Now - _lastCheck).TotalMinutes < cacheMinutes)
                {
                    return _lastStatus;
                }
            }

            var status = await CheckNowAsync();

            lock (_lock)
            {
                _lastStatus = status;
                _lastCheck = DateTime.Now;
            }

            return status;
        }

        public static async Task<SmtpStatus> CheckNowAsync()
        {
            EmailConfig cfg;
            try
            {
                cfg = EmailConfig.Load();
            }
            catch
            {
                return SmtpStatus.NotConfigured;
            }

            // 1. Champs manquants
            if (string.IsNullOrWhiteSpace(cfg.Server) ||
                string.IsNullOrWhiteSpace(cfg.From) ||
                string.IsNullOrWhiteSpace(cfg.Password) ||
                string.IsNullOrWhiteSpace(cfg.To) ||
                cfg.Port <= 0)
            {
                return SmtpStatus.NotConfigured;
            }

            // 2. Test du port
            try
            {
                using var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync(cfg.Server, cfg.Port);
                var timeoutTask = Task.Delay(3000);

                if (await Task.WhenAny(connectTask, timeoutTask) != connectTask)
                    return SmtpStatus.Unreachable;

                if (!tcp.Connected)
                    return SmtpStatus.Unreachable;
            }
            catch
            {
                return SmtpStatus.Unreachable;
            }

            // 3. Test SMTP complet (connexion + auth)
            try
            {
                var options = cfg.SecurityMode.ToUpper() switch
                {
                    "SSL"      => SecureSocketOptions.SslOnConnect,
                    "TLS"      => SecureSocketOptions.StartTls,
                    "STARTTLS" => SecureSocketOptions.StartTls,
                    "NONE"     => SecureSocketOptions.None,
                    _          => SecureSocketOptions.Auto
                };

                using var client = new SmtpClient();
                await client.ConnectAsync(cfg.Server, cfg.Port, options);
                await client.AuthenticateAsync(cfg.From, cfg.Password);
                await client.DisconnectAsync(true);

                return SmtpStatus.Ready;
            }
            catch (MailKit.Security.AuthenticationException)
            {
                return SmtpStatus.AuthFailed;
            }
            catch
            {
                return SmtpStatus.Unknown;
            }
        }

        /// <summary>
        /// Force le statut à « Ready » (utile après un test SMTP réussi depuis l'UI).
        /// </summary>
        public static void MarkAsReady()
        {
            lock (_lock)
            {
                _lastStatus = SmtpStatus.Ready;
                _lastCheck = DateTime.Now;
            }
        }
    }
}