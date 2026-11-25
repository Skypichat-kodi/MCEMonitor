using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using NetMonitor.Service.Models;

namespace NetMonitor.Service.Services;

public sealed class GeoIpService : IDisposable
{
    private readonly ConcurrentDictionary<string, GeoInfo> _cache = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly SemaphoreSlim _throttle = new(2, 2);   // �tait new(5, 5)
    private readonly string _providerTemplate;
    private readonly bool _enabled;
    private readonly bool _reverseDnsEnabled;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);
    private volatile bool _disposed;

    // Cache persistant : sans lui, chaque redémarrage redemandait la géoloc de
    // toutes les IP déjà résolues, et le quota de l API partait en fumée.
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "MCEMonitor", "NetMonitor", "geoip_cache.json");

    private const int CacheMaxEntrees = 5000;
    private static readonly TimeSpan DelaiSauvegarde = TimeSpan.FromSeconds(10);

    private readonly object _sauvegardeLock = new();
    private DateTime _derniereSauvegarde = DateTime.MinValue;
    private bool _cacheModifie;

    public bool IsEnabled => _enabled;
    
    // Quota API �puis� (429) � partag� avec NetMonitorEngine pour reporter les lots
    private DateTime _quotaExceededUntil = DateTime.MinValue;
    public bool IsQuotaExceeded => DateTime.UtcNow < _quotaExceededUntil;

    /// <summary>Date de reprise des resolutions, ou DateTime.MinValue si tout va bien.</summary>
    public DateTime QuotaResumeAt => _quotaExceededUntil;

    /// <summary>Nombre d entrees valides actuellement en cache (memoire).</summary>
    public int CacheCount => _cache.Count;
    
    public GeoIpService(string providerTemplate, bool enabled, bool reverseDnsEnabled)
    {
        _providerTemplate = string.IsNullOrWhiteSpace(providerTemplate)
            ? "https://ipwho.is/{ip}"
            : providerTemplate;
        _enabled = enabled;
        _reverseDnsEnabled = reverseDnsEnabled;

        ChargerCacheDisque();
    }

    /// <summary>
    /// Relit le cache écrit au dernier arrêt. Les entrées trop anciennes sont
    /// ignorées : elles seront résolues à nouveau au premier besoin.
    /// </summary>
    private void ChargerCacheDisque()
    {
        try
        {
            if (!File.Exists(CachePath)) return;

            var entrees = JsonSerializer.Deserialize<Dictionary<string, GeoInfo>>(File.ReadAllText(CachePath));
            if (entrees == null) return;

            int reprises = 0;

            foreach (var (ip, info) in entrees)
            {
                if (info == null || string.IsNullOrWhiteSpace(info.CountryCode)) continue;
                if (DateTime.UtcNow - info.ResolvedAt >= CacheTtl) continue;

                _cache[ip] = info;
                reprises++;
            }

            if (reprises > 0)
                CoreLog.Info($"[GEOIP] Cache disque : {reprises} IP(s) reprises");
        }
        catch (Exception ex) { CoreLog.Warn($"[GEOIP] Lecture du cache impossible : {ex.Message}"); }
    }

    /// <summary>
    /// Écrit le cache sur disque, au plus une fois toutes les dix secondes : une
    /// résolution par IP ne doit pas se payer d une écriture de fichier.
    /// </summary>
    private void SauvegarderCacheDisque(bool force = false)
    {
        lock (_sauvegardeLock)
        {
            if (!_cacheModifie) return;
            if (!force && DateTime.UtcNow - _derniereSauvegarde < DelaiSauvegarde) return;

            try
            {
                // On ne garde que les entrées valides, les plus récentes d abord.
                var aGarder = _cache
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.CountryCode)
                              && DateTime.UtcNow - kv.Value.ResolvedAt < CacheTtl)
                    .OrderByDescending(kv => kv.Value.ResolvedAt)
                    .Take(CacheMaxEntrees)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);

                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                File.WriteAllText(CachePath, JsonSerializer.Serialize(aGarder));

                _cacheModifie = false;
                _derniereSauvegarde = DateTime.UtcNow;
            }
            catch (Exception ex) { CoreLog.Warn($"[GEOIP] Écriture du cache impossible : {ex.Message}"); }
        }
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
    /// Amorce le cache avec une g�oloc d�j� connue (typiquement lue depuis
    /// blocked_ips.json ou alert_history.json au d�marrage).
    /// Ne fait rien si CountryCode est vide (pas de g�oloc valide � seeder).
    /// </summary>
    public void SeedCache(string ip, GeoInfo info)
    {
        if (string.IsNullOrWhiteSpace(ip) || info == null) return;
        if (string.IsNullOrWhiteSpace(info.CountryCode)) return;
        _cache[ip] = info;
    }

    /// <summary>
    /// Invalide l'entr�e de cache d'une IP (force un re-fetch au prochain ResolveAsync).
    /// Utilis� par le refresh p�riodique.
    /// </summary>
    public void InvalidateCache(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        _cache.TryRemove(ip, out _);
    }
    
    public void ResolveInBackground(string ip)
    {
        if (_disposed) return;
        if (!_enabled) return;
        if (string.IsNullOrWhiteSpace(ip)) return;

        // Ne pas relancer si on a d�j� une entr�e valide (non expir�e, avec CountryCode)
        if (_cache.TryGetValue(ip, out var existing) &&
            DateTime.UtcNow - existing.ResolvedAt < CacheTtl &&
            !string.IsNullOrEmpty(existing.CountryCode))
            return;

        _ = ResolveAsync(ip);
    }

    public async Task<GeoInfo> ResolveAsync(string ip, bool includeReverseDns = true)
    {
        if (_disposed)
            return new GeoInfo { Ip = ip, Country = "?", CountryCode = "", ResolvedAt = DateTime.MinValue };    
        if (string.IsNullOrWhiteSpace(ip))
            return new GeoInfo { Ip = ip, Country = "?", ResolvedAt = DateTime.UtcNow };

        // Quota �puis� : ne pas appeler l'API, retourner un objet transitoire non cach�
        if (DateTime.UtcNow < _quotaExceededUntil)
            return new GeoInfo { Ip = ip, Country = "?", CountryCode = "", ResolvedAt = DateTime.MinValue };
        
        // Cache hit : seulement si CountryCode non vide (sinon = �chec cach�)
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
            var disabled = new GeoInfo { Ip = ip, Country = "(d�sactiv�)", ResolvedAt = DateTime.UtcNow };
            _cache[ip] = disabled;
            return disabled;
        }

        await _throttle.WaitAsync();
        try
        {
            // Re-check sous le s�maphore (un autre thread a pu r�soudre entre-temps)
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
                // ? �chec : on retourne un objet "transitoire" SANS le mettre en cache.
                //    FinalizeBlockedIpGeoAsync le verra avec CountryCode vide et retentera.
                //    (L'appelant peut diff�rencier via ResolvedAt == DateTime.MinValue)
                return new GeoInfo
                {
                    Ip = ip,
                    Country = "?",
                    CountryCode = "",
                    ResolvedAt = DateTime.MinValue   // ? marqueur "pas r�solu, pas cach�"
                };
            }

            fetched.Ip = ip;
            fetched.ResolvedAt = DateTime.UtcNow;

            if (_reverseDnsEnabled && includeReverseDns)
                fetched.ReverseDns = await TryReverseDnsAsync(ip);

            _cache[ip] = fetched;
            _cacheModifie = true;
            SauvegarderCacheDisque();   // ? cache uniquement sur succ�s
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
                    // ipwho.is : quota journalier ? pause de 24h par d�faut
                    // (Retry-After si pr�sent, mais souvent absent ou court)
                    // On respecte le delai demande par l API : le plancher de 24 h
                    // prolongeait la pause au-dela de ce qu elle reclame.
                    var retryAfter = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromHours(1);

                    if (retryAfter < TimeSpan.FromMinutes(5))
                        retryAfter = TimeSpan.FromMinutes(5);

                    if (retryAfter > TimeSpan.FromHours(48))
                        retryAfter = TimeSpan.FromHours(48);

                    _quotaExceededUntil = DateTime.UtcNow + retryAfter;
                    CoreLog.Warn($"[GEOIP] Quota �puis� � pause jusqu'� {_quotaExceededUntil.ToLocalTime():yyyy-MM-dd HH:mm}");
                    return null;
                }

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False)
            {
                string msg = GetStr(root, "message");
                CoreLog.Warn($"[GEOIP] {ip} refus� par l'API : {(string.IsNullOrEmpty(msg) ? "raison inconnue" : msg)}");
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

            // ? Si le code pays est vide alors que l'API a r�pondu "success",
            //    c'est suspect : on log mais on accepte (IP sans g�oloc connue)
            if (string.IsNullOrEmpty(code))
            {
                CoreLog.Warn($"[GEOIP] {ip} succ�s mais country_code vide (country='{country}')");
                return null;   // ? on pr�f�re re-tenter plus tard
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
    
    // ============================================================
    //  IDisposable � lib�re le pool HTTP et le s�maphore
    // ============================================================
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SauvegarderCacheDisque(force: true);

        try { _http.Dispose(); }
        catch (Exception ex) { CoreLog.Warn($"GeoIpService.Dispose (http) : {ex.Message}"); }

        try { _throttle.Dispose(); }
        catch (Exception ex) { CoreLog.Warn($"GeoIpService.Dispose (throttle) : {ex.Message}"); }

        CoreLog.Info("GeoIpService dispos�");
    }    
}