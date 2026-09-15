using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MediaMonitor.Core.Services
{
    /// <summary>
    /// Enveloppe autour de Dns.GetHostEntry avec un cache mémoire.
    /// Le comportement est identique à l'appel direct, mais les résultats
    /// sont mémorisés pendant 10 minutes.
    /// </summary>
    public static class DnsResolver
    {
        private class CacheEntry
        {
            public string HostName { get; set; } = "";
            public string ResolvedIp { get; set; } = "";
            public DateTime Expiry { get; set; }
        }

        private static readonly Dictionary<string, CacheEntry> _cache =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new();

        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Équivalent à Dns.GetHostEntry(input), mais avec cache.
        /// Renvoie :
        ///  - HostName    : le hostname résolu (ou l'input si échec)
        ///  - ResolvedIp  : l'IP résolue (ou l'input si échec)
        ///  - Success     : true si la résolution a réussi
        /// </summary>
        public static (string HostName, string ResolvedIp, bool Success) Resolve(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return (input ?? "", input ?? "", false);

            // Cache hit ?
            lock (_lock)
            {
                if (_cache.TryGetValue(input, out var cached) && cached.Expiry > DateTime.Now)
                    return (cached.HostName, cached.ResolvedIp, true);
            }

            try
            {
                var entry = Dns.GetHostEntry(input);

                string hostName = entry.HostName ?? input;

                var v4 = Array.Find(entry.AddressList,
                    a => a.AddressFamily == AddressFamily.InterNetwork);

                string resolvedIp = v4?.ToString() ?? input;

                lock (_lock)
                {
                    _cache[input] = new CacheEntry
                    {
                        HostName = hostName,
                        ResolvedIp = resolvedIp,
                        Expiry = DateTime.Now.Add(CacheTtl)
                    };
                }

                return (hostName, resolvedIp, true);
            }
            catch
            {
                return (input, input, false);
            }
        }

        /// <summary>
        /// Vide le cache (utile en cas de changement réseau).
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _cache.Clear();
            }
        }
    }
}