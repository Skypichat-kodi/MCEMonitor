using System;
using System.Collections.Generic;
using MCEMonitorClient.Shared.Models;

namespace MCEMonitorClient.Maui.Services
{
    /// <summary>
    /// Stocke le dernier état connu de chaque serveur, partagé entre
    /// le Foreground Service et l'UI.
    /// </summary>
    public static class PollingState
    {
        private static readonly Dictionary<string, string> _statusByServerId = new();
        private static readonly object _lock = new();

        public static void Update(IEnumerable<PollResult> results)
        {
            lock (_lock)
            {
                _statusByServerId.Clear();
                foreach (var r in results)
                {
                    string status = r.Online
                        ? (string.IsNullOrEmpty(r.Status) ? "ok" : r.Status)
                        : "offline";
                    _statusByServerId[r.ServerId] = status;
                }
            }
        }

        public static string GetStatus(string serverId)
        {
            lock (_lock)
            {
                return _statusByServerId.TryGetValue(serverId, out var s) ? s : "unknown";
            }
        }
    }
}