using System;
using System.Collections.Concurrent;

namespace NetMonitor.Service.Web
{
    /// <summary>
    /// Sessions OTP en mémoire. Une session est créée après validation
    /// réussie d'un code OTP et expire après N minutes (défaut 15).
    /// </summary>
    public static class OtpSessionStore
    {
        private static readonly ConcurrentDictionary<string, DateTime> _sessions = new();

        public static string Create(int minutes)
        {
            string token = Guid.NewGuid().ToString("N");
            _sessions[token] = DateTime.UtcNow.AddMinutes(minutes);
            return token;
        }

        public static bool IsValid(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            if (!_sessions.TryGetValue(token, out var expires)) return false;

            if (expires < DateTime.UtcNow)
            {
                _sessions.TryRemove(token, out _);
                return false;
            }
            return true;
        }

        public static void Invalidate(string? token)
        {
            if (!string.IsNullOrWhiteSpace(token))
                _sessions.TryRemove(token, out _);
        }

        /// <summary>Purge les sessions expirées (appelé périodiquement).</summary>
        public static void PurgeExpired()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _sessions)
            {
                if (kv.Value < now)
                    _sessions.TryRemove(kv.Key, out _);
            }
        }
    }
}