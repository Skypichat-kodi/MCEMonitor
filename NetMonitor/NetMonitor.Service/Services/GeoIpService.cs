using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using NetMonitor.Service.Models;

namespace NetMonitor.Service.Services;

public sealed class GeoIpService
{
    private readonly ConcurrentDictionary<string, GeoInfo> _cache = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly SemaphoreSlim _throttle = new(5, 5);
    private readonly string _providerTemplate;
    private readonly bool _enabled;
    private readonly bool _reverseDnsEnabled;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    public GeoIpService(string providerTemplate, bool enabled, bool reverseDnsEnabled)
    {
        _providerTemplate = string.IsNullOrWhiteSpace(providerTemplate)
            ? "https://ipwho.is/{ip}"
            : providerTemplate;
        _enabled = enabled;
        _reverseDnsEnabled = reverseDnsEnabled;
    }

    public GeoInfo? TryGetCached(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        if (_cache.TryGetValue(ip, out var info) &&
            DateTime.UtcNow - info.ResolvedAt < CacheTtl)
            return info;
        return null;
    }

    public void ResolveInBackground(string ip)
    {
        if (!_enabled) return;
        if (string.IsNullOrWhiteSpace(ip)) return;
        if (_cache.ContainsKey(ip)) return;
        _ = ResolveAsync(ip);
    }

    public async Task<GeoInfo> ResolveAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return new GeoInfo { Ip = ip, Country = "?", ResolvedAt = DateTime.UtcNow };

        if (_cache.TryGetValue(ip, out var cached) &&
            DateTime.UtcNow - cached.ResolvedAt < CacheTtl)
            return cached;

        if (IsPrivateIp(ip))
        {
            var priv = GeoInfo.Private(ip);
            _cache[ip] = priv;
            return priv;
        }

        if (!_enabled)
        {
            var disabled = new GeoInfo { Ip = ip, Country = "(désactivé)", ResolvedAt = DateTime.UtcNow };
            _cache[ip] = disabled;
            return disabled;
        }

        await _throttle.WaitAsync();
        try
        {
            if (_cache.TryGetValue(ip, out cached) &&
                DateTime.UtcNow - cached.ResolvedAt < CacheTtl)
                return cached;

            var info = await FetchGeoAsync(ip);
            info.Ip = ip;
            info.ResolvedAt = DateTime.UtcNow;

            if (_reverseDnsEnabled)
                info.ReverseDns = await TryReverseDnsAsync(ip);

            _cache[ip] = info;
            return info;
        }
        finally { _throttle.Release(); }
    }

    private async Task<GeoInfo> FetchGeoAsync(string ip)
    {
        try
        {
            string url = _providerTemplate.Replace("{ip}", Uri.EscapeDataString(ip));
            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                return new GeoInfo { Country = "?", CountryCode = "" };

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False)
                return new GeoInfo { Country = "?", CountryCode = "" };

            string country = GetStr(root, "country");
            string code = GetStr(root, "country_code");
            string city = GetStr(root, "city");
            string isp = "";
            string org = "";

            if (root.TryGetProperty("connection", out var conn))
            {
                isp = GetStr(conn, "isp");
                org = GetStr(conn, "org");
            }

            return new GeoInfo
            {
                Country = country,
                CountryCode = code,
                City = city,
                Isp = isp,
                Org = org
            };
        }
        catch (Exception ex)
        {
            CoreLog.Warn($"GeoIP lookup failed for {ip}: {ex.Message}");
            return new GeoInfo { Country = "?", CountryCode = "" };
        }
    }

    private static async Task<string?> TryReverseDnsAsync(string ip)
    {
        try
        {
            var task = Dns.GetHostEntryAsync(ip);
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));
            if (completed != task) return null;
            var entry = await task;
            return string.IsNullOrWhiteSpace(entry.HostName) ? null : entry.HostName;
        }
        catch { return null; }
    }

    private static string GetStr(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? "";
        return "";
    }

    public static bool IsPrivateIp(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return false;
        if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;

        var b = addr.GetAddressBytes();
        if (b[0] == 10) return true;
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
        if (b[0] == 192 && b[1] == 168) return true;
        if (b[0] == 127) return true;
        if (b[0] == 169 && b[1] == 254) return true;
        if (b[0] == 0) return true;
        return false;
    }

    public void Clear() => _cache.Clear();
}