using System;
using System.Net;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NetMonitor.Service.Models;

namespace NetMonitor.Service.Detectors;

/// <summary>
/// État partagé entre détecteurs : fenêtres glissantes, caches, seuils.
/// Éviction automatique pour éviter les fuites mémoire sur un service longue durée.
/// </summary>
public sealed class DetectorContext
{
    private readonly ConcurrentDictionary<string, SlidingWindow<int>> _synPorts = new();
    private readonly ConcurrentDictionary<int, SlidingWindow<DateTime>> _beaconHits = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastSeenIp = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastAlertByIp = new();
    private readonly ConcurrentDictionary<string, SlidingWindow<DateTime>> _synPackets = new();
    private readonly ConcurrentDictionary<string, SlidingWindow<DateTime>> _icmpPackets = new();    

    public NetMonitorSettings Settings { get; }

    public DetectorContext(NetMonitorSettings settings)
    {
        Settings = settings;
    }

    // ---- PortScan : associe une IP source distante à la fenêtre de ports SYN observés ----
    public IReadOnlyCollection<int> RecordSynPort(string remoteIp, int port, TimeSpan window)
    {
        var w = _synPorts.GetOrAdd(remoteIp, _ => new SlidingWindow<int>(window));
        w.Add(port);
        _lastSeenIp[remoteIp] = DateTime.UtcNow;
        return w.Snapshot();
    }

    // ---- Beaconing : horodatage des connexions par PID ----
    public IReadOnlyCollection<DateTime> RecordBeaconHit(int pid, TimeSpan window)
    {
        var w = _beaconHits.GetOrAdd(pid, _ => new SlidingWindow<DateTime>(window));
        w.Add(DateTime.UtcNow);
        return w.Snapshot();
    }

    /// <summary>À appeler périodiquement (ex : toutes les 5 min) depuis le moteur.</summary>
    public void EvictStale(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var kv in _lastSeenIp)
            if (kv.Value < cutoff) { _synPorts.TryRemove(kv.Key, out _); _lastSeenIp.TryRemove(kv.Key, out _); }
        foreach (var kv in _beaconHits)
            if (kv.Value.IsEmpty) _beaconHits.TryRemove(kv.Key, out _);            
        foreach (var kv in _lastAlertByIp)
            if (kv.Value < cutoff) _lastAlertByIp.TryRemove(kv.Key, out _);            
        foreach (var kv in _synPackets)
            if (kv.Value.IsEmpty) _synPackets.TryRemove(kv.Key, out _);
        foreach (var kv in _icmpPackets)
            if (kv.Value.IsEmpty) _icmpPackets.TryRemove(kv.Key, out _);                        
    }

    /// <summary>
    /// Efface l'historique de détection pour une IP donnée.
    /// Appelé quand une IP est bloquée : les détecteurs repartent de zéro.
    /// </summary>
    public void ForgetIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;

        _synPorts.TryRemove(ip, out _);
        _lastSeenIp.TryRemove(ip, out _);
        _lastAlertByIp.TryRemove(ip, out _);
        
        // Note : le beaconing étant indexé par PID, on ne peut pas 
        // le purger par IP, mais le NetMonitorEngine filtrera les alertes 
        // si l'IP est bloquée.
    }

    /// <summary>Retourne true si on peut émettre une alerte pour cette IP (respect d'un cooldown).</summary>
    public bool TryAcquireAlertSlot(string remoteIp, TimeSpan cooldown)
    {
        var now = DateTime.UtcNow;
        if (_lastAlertByIp.TryGetValue(remoteIp, out var last) && (now - last) < cooldown)
            return false;

        _lastAlertByIp[remoteIp] = now;
        return true;
    }    

    /// <summary>Fenêtre glissante thread-safe basée sur un timestamp par item.</summary>
    private sealed class SlidingWindow<T>
    {
        private readonly TimeSpan _window;
        private readonly object _lock = new();
        private readonly LinkedList<(DateTime Ts, T Value)> _items = new();

        public SlidingWindow(TimeSpan window) => _window = window;

        public void Add(T value)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                _items.AddLast((now, value));
                var cutoff = now - _window;
                while (_items.First is { } first && first.Value.Ts < cutoff)
                    _items.RemoveFirst();
            }
        }

        public IReadOnlyCollection<T> Snapshot()
        {
            lock (_lock) return _items.Select(i => i.Value).ToArray();
        }

        public bool IsEmpty { get { lock (_lock) return _items.Count == 0; } }
        
        public int Count { get { lock (_lock) return _items.Count; } }
    }
    
    public int RecordSynPacket(string sourceIp, TimeSpan window)
    {
        var w = _synPackets.GetOrAdd(sourceIp, _ => new SlidingWindow<DateTime>(window));
        w.Add(DateTime.UtcNow);
        return w.Count;
    }

    public int RecordIcmpPacket(string sourceIp, TimeSpan window)
    {
        var w = _icmpPackets.GetOrAdd(sourceIp, _ => new SlidingWindow<DateTime>(window));
        w.Add(DateTime.UtcNow);
        return w.Count;
    }
    
    /// <summary>
    /// Vérifie si une IP appartient à une plage ignorée (ex: réseau local).
    /// </summary>
    public bool IsIgnoredNetwork(string ip)
    {
        // Mode Debug : on ignore les filtres
        if (Settings.DisableFilters) return false;

        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out var addr))
            return false;

        foreach (var cidr in Settings.IgnoredNetworks)
        {
            if (IsInCidr(addr, cidr)) return true;
        }
        return false;
    }

    private static bool IsInCidr(IPAddress ip, string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var network)) return false;
        if (!int.TryParse(parts[1], out int prefixLength)) return false;

        var ipBytes = ip.GetAddressBytes();
        var netBytes = network.GetAddressBytes();

        if (ipBytes.Length != netBytes.Length) return false;

        int fullBytes = prefixLength / 8;
        int remainingBits = prefixLength % 8;

        for (int i = 0; i < fullBytes; i++)
        {
            if (ipBytes[i] != netBytes[i]) return false;
        }

        if (remainingBits > 0)
        {
            int mask = 0xFF << (8 - remainingBits);
            if ((ipBytes[fullBytes] & mask) != (netBytes[fullBytes] & mask))
                return false;
        }

        return true;
    }        
}