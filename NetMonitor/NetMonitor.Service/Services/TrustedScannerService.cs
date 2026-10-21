using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NetMonitor.Service.Services;

/// <summary>
/// Détecte les scanners de recherche sécurité légitimes (Shadowserver, Censys,
/// Shodan, etc.) via reverse DNS. Cache 24 h pour éviter les lookups répétés.
/// </summary>
public sealed class TrustedScannerService
{
    private readonly ConcurrentDictionary<string, (bool IsTrusted, DateTime CheckedAt)> _cache = new();
    private readonly SemaphoreSlim _throttle = new(4, 4);
    private static readonly TimeSpan CacheTtl  = TimeSpan.FromHours(24);
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(2);
    private readonly string[] _patterns;

    public TrustedScannerService(string[] patterns)
    {
        _patterns = patterns ?? Array.Empty<string>();
    }

    /// <summary>Retourne null si l'IP n'est pas en cache, sinon true/false.</summary>
    public bool? TryGetCached(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        if (_cache.TryGetValue(ip, out var entry) && DateTime.UtcNow - entry.CheckedAt < CacheTtl)
            return entry.IsTrusted;
        return null;
    }

    /// <summary>Lance la vérification en tâche de fond (fire-and-forget).</summary>
    public void CheckInBackground(string ip)
    {
        if (TryGetCached(ip).HasValue) return;
        _ = Task.Run(async () => await IsTrustedScannerAsync(ip));
    }

    public async Task<bool> IsTrustedScannerAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        if (!IPAddress.TryParse(ip, out _)) return false;
        if (_patterns.Length == 0) return false;

        var cached = TryGetCached(ip);
        if (cached.HasValue) return cached.Value;

        await _throttle.WaitAsync();
        try
        {
            cached = TryGetCached(ip);
            if (cached.HasValue) return cached.Value;

            bool isTrusted = await ReverseDnsCheckAsync(ip);
            _cache[ip] = (isTrusted, DateTime.UtcNow);

            if (isTrusted)
                CoreLog.Debug($"[SCANNER] IP {ip} identifiée comme scanner légitime");

            return isTrusted;
        }
        finally { _throttle.Release(); }
    }

    private async Task<bool> ReverseDnsCheckAsync(string ip)
    {
        try
        {
            var task = Dns.GetHostEntryAsync(ip);
            var completed = await Task.WhenAny(task, Task.Delay(DnsTimeout));
            if (completed != task) return false;

            var entry = await task;
            string hostname = (entry.HostName ?? "").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(hostname)) return false;

            return _patterns.Any(p =>
                !string.IsNullOrWhiteSpace(p) &&
                hostname.Contains(p.Trim().ToLowerInvariant()));
        }
        catch
        {
            return false;
        }
    }

    public void Clear() => _cache.Clear();
}