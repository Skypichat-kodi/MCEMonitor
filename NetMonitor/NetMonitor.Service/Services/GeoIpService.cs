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
    private readonly SemaphoreSlim _throttle = new(2, 2);   // était new(5, 5)
    private readonly string _providerTemplate;
    private readonly bool _enabled;
    private readonly bool _reverseDnsEnabled;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    public bool IsEnabled => _enabled;
    
    // Quota API épuisé (429) — partagé avec NetMonitorEngine pour reporter les lots
    private DateTime _quotaExceededUntil = DateTime.MinValue;
    public bool IsQuotaExceeded => DateTime.UtcNow < _quotaExceededUntil;    
    
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
    
    /// <summary>
    /// Amorce le cache avec une géoloc déjà connue (typiquement lue depuis
    /// blocked_ips.json ou alert_history.json au démarrage).
    /// Ne fait rien si CountryCode est vide (pas de géoloc valide à seeder).
    /// </summary>
    public void SeedCache(string ip, GeoInfo info)
    {
        if (string.IsNullOrWhiteSpace(ip) || info == null) return;
        if (string.IsNullOrWhiteSpace(info.CountryCode)) return;
        _cache[ip] = info;
    }

    /// <summary>
    /// Invalide l'entrée de cache d'une IP (force un re-fetch au prochain ResolveAsync).
    /// Utilisé par le refresh périodique.
    /// </summary>
    public void InvalidateCache(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        _cache.TryRemove(ip, out _);
    }
    
    public void ResolveInBackground(string ip)
    {
        if (!_enabled) return;
        if (string.IsNullOrWhiteSpace(ip)) return;

        // Ne pas relancer si on a déjà une entrée valide (non expirée, avec CountryCode)
        if (_cache.TryGetValue(ip, out var existing) &&
            DateTime.UtcNow - existing.ResolvedAt < CacheTtl &&
            !string.IsNullOrEmpty(existing.CountryCode))
            return;

        _ = ResolveAsync(ip);
    }

    public async Task<GeoInfo> ResolveAsync(string ip, bool includeReverseDns = true)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return new GeoInfo { Ip = ip, Country = "?", ResolvedAt = DateTime.UtcNow };

        // Quota épuisé : ne pas appeler l'API, retourner un objet transitoire non caché
        if (DateTime.UtcNow < _quotaExceededUntil)
            return new GeoInfo { Ip = ip, Country = "?", CountryCode = "", ResolvedAt = DateTime.MinValue };
        
        // Cache hit : seulement si CountryCode non vide (sinon = échec caché)
        if (_cache.TryGetValue(ip, out var cached) &&
            DateTime.UtcNow - cached.ResolvedAt < CacheTtl &&
            !string.IsNullOrEmpty(cached.CountryCode))
        {
            if (!includeReverseDns || !string.IsNullOrEmpty(cached.ReverseDns) || !_reverseDnsEnabled)
                return cached;
        }

        if (!IPAddress.TryParse(ip, out _))
        {
            // IP invalide : on peut cacher, c'est stable
            var invalid = new GeoInfo { Ip = ip, Country = "?", CountryCode = "", ResolvedAt = DateTime.UtcNow };
            _cache[ip] = invalid;
            return invalid;
        }

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
            // Re-check sous le sémaphore (un autre thread a pu résoudre entre-temps)
            if (_cache.TryGetValue(ip, out cached) &&
                DateTime.UtcNow - cached.ResolvedAt < CacheTtl &&
                !string.IsNullOrEmpty(cached.CountryCode))
            {
                if (!includeReverseDns || !string.IsNullOrEmpty(cached.ReverseDns) || !_reverseDnsEnabled)
                    return cached;
            }

            var fetched = await FetchGeoAsync(ip);

            if (fetched == null)
            {
                // ? Échec : on retourne un objet "transitoire" SANS le mettre en cache.
                //    FinalizeBlockedIpGeoAsync le verra avec CountryCode vide et retentera.
                //    (L'appelant peut différencier via ResolvedAt == DateTime.MinValue)
                return new GeoInfo
                {
                    Ip = ip,
                    Country = "?",
                    CountryCode = "",
                    ResolvedAt = DateTime.MinValue   // ? marqueur "pas résolu, pas caché"
                };
            }

            fetched.Ip = ip;
            fetched.ResolvedAt = DateTime.UtcNow;

            if (_reverseDnsEnabled && includeReverseDns)
                fetched.ReverseDns = await TryReverseDnsAsync(ip);

            _cache[ip] = fetched;   // ? cache uniquement sur succès
            return fetched;
        }
        finally { _throttle.Release(); }
    }

    private async Task<GeoInfo?> FetchGeoAsync(string ip)
    {
        try
        {
            string url = _providerTemplate.Replace("{ip}", Uri.EscapeDataString(ip));
            using var resp = await _http.GetAsync(url);

                if ((int)resp.StatusCode == 429)
                {
                    // ipwho.is : quota journalier ? pause de 24h par défaut
                    // (Retry-After si présent, mais souvent absent ou court)
                    var retryAfter = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromHours(24);
                    if (retryAfter < TimeSpan.FromHours(24))
                        retryAfter = TimeSpan.FromHours(24);   // forcer 24h minimum

                    _quotaExceededUntil = DateTime.UtcNow + retryAfter;
                    CoreLog.Warn($"[GEOIP] Quota épuisé — pause jusqu'à {_quotaExceededUntil.ToLocalTime():yyyy-MM-dd HH:mm}");
                    return null;
                }

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False)
            {
                string msg = GetStr(root, "message");
                CoreLog.Warn($"[GEOIP] {ip} refusé par l'API : {(string.IsNullOrEmpty(msg) ? "raison inconnue" : msg)}");
                return null;   // ? PAS de cache
            }

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

            // ? Si le code pays est vide alors que l'API a répondu "success",
            //    c'est suspect : on log mais on accepte (IP sans géoloc connue)
            if (string.IsNullOrEmpty(code))
            {
                CoreLog.Warn($"[GEOIP] {ip} succès mais country_code vide (country='{country}')");
                return null;   // ? on préfère re-tenter plus tard
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
        catch (TaskCanceledException)
        {
            CoreLog.Warn($"[GEOIP] {ip} timeout (> {_http.Timeout.TotalSeconds:F0}s)");
            return null;
        }
        catch (Exception ex)
        {
            CoreLog.Warn($"[GEOIP] {ip} erreur : {ex.Message}");
            return null;
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