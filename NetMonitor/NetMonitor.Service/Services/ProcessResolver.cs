using System.Collections.Concurrent;
using System.Diagnostics;

namespace NetMonitor.Service.Services;

public static class ProcessResolver
{
    private static readonly ConcurrentDictionary<int, (string Name, DateTime CachedAt)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public static string Resolve(int pid)
    {
        if (pid <= 0) return "?";
        if (pid == 4) return "System";

        if (_cache.TryGetValue(pid, out var entry) && DateTime.UtcNow - entry.CachedAt < CacheTtl)
            return entry.Name;

        try
        {
            using var p = Process.GetProcessById(pid);
            var name = p.ProcessName;
            _cache[pid] = (name, DateTime.UtcNow);
            return name;
        }
        catch
        {
            _cache[pid] = ("<inaccessible>", DateTime.UtcNow);
            return "<inaccessible>";
        }
    }

    public static void ClearCache() => _cache.Clear();
}