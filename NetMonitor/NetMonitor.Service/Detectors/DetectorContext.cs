using System.Collections.Concurrent;
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
    }
    
    private readonly ConcurrentDictionary<string, DateTime> _lastAlertByIp = new();

    /// <summary>
    /// Retourne true si on peut émettre une alerte pour cette IP
    /// (respect d'un cooldown de N secondes).
    /// </summary>
    public bool TryAcquireAlertSlot(string remoteIp, TimeSpan cooldown)
    {
        var now = DateTime.UtcNow;
        if (_lastAlertByIp.TryGetValue(remoteIp, out var last) && (now - last) < cooldown)
            return false;

        _lastAlertByIp[remoteIp] = now;
        return true;
    }    
}