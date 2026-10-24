using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace NetMonitor.Service.Capture
{
    /// <summary>
    /// Fournit la liste des IPs locales de la machine (cache 30 s).
    /// Utilisé pour déterminer le sens d'un paquet capturé par WinDivert.
    /// </summary>
    public static class LocalIps
    {
        private static HashSet<IPAddress> _cache = new();
        private static DateTime _lastRefresh = DateTime.MinValue;
        private static readonly object _lock = new();
        private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

        public static bool IsLocal(IPAddress ip)
        {
            if (ip == null) return false;
            if (IPAddress.IsLoopback(ip)) return true;

            RefreshIfNeeded();

            lock (_lock)
                return _cache.Contains(ip);
        }

        private static void RefreshIfNeeded()
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _lastRefresh < Ttl) return;

                try
                {
                    var set = new HashSet<IPAddress>();
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != OperationalStatus.Up) continue;

                        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                            set.Add(ua.Address);
                    }
                    _cache = set;
                }
                catch (Exception ex)
                {
                    CoreLog.Warn($"LocalIps.Refresh : {ex.Message}");
                }

                _lastRefresh = DateTime.UtcNow;
            }
        }
    }
}