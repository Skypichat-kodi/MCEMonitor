using System;
using System.Collections.Concurrent;

namespace SystemMonitor.Service.Web
{
    /// <summary>
    /// Sessions de login (auth simple, pas d'OTP).
    /// Créées après un POST /login réussi, valables 15 min par défaut.
    /// </summary>
    public static class SessionStore
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
    }
}