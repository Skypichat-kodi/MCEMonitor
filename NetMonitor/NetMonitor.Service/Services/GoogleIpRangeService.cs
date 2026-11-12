using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NetMonitor.Service.Services
{
    /// <summary>
    /// Télécharge et maintient en cache les plages IP officielles Google
    /// (crawlers communs, spéciaux et user-triggered).
    /// </summary>
    public sealed class GoogleIpRangeService : IDisposable
    {
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "NetMonitor");
        private static readonly string CacheFile = Path.Combine(CacheDir, "google-ip-ranges.json");

        // Nouvelles URLs (Google a migré de /search/apis/ vers /crawling/)
        private const string CommonUrl  = "https://developers.google.com/crawling/ipranges/common-crawlers.json";
        private const string SpecialUrl = "https://developers.google.com/crawling/ipranges/special-crawlers.json";
        private const string UserUrl    = "https://developers.google.com/crawling/ipranges/user-triggered-fetchers.json";

        private readonly object _lock = new();
        private List<IpCidr> _common   = new();
        private List<IpCidr> _special  = new();
        private List<IpCidr> _user     = new();
        private DateTime _lastUpdate   = DateTime.MinValue;
        private bool _isLoaded         = false;

        private readonly bool _enabled;
        private readonly int _refreshHours;
        private Timer? _timer;
        private readonly SemaphoreSlim _downloadLock = new(1, 1);

        public bool IsEnabled => _enabled;
        public bool IsLoaded { get { lock (_lock) return _isLoaded; } }
        public DateTime LastUpdate { get { lock (_lock) return _lastUpdate; } }
        public int RangeCount { get { lock (_lock) return _common.Count + _special.Count + _user.Count; } }

        public GoogleIpRangeService(bool enabled, int refreshHours)
        {
            _enabled = enabled;
            _refreshHours = Math.Max(1, refreshHours);
        }

        public void Start()
        {
            if (!_enabled)
            {
                CoreLog.Info("GoogleIpRange : désactivé");
                return;
            }

            TryLoadFromCache();

            _ = Task.Run(async () =>
            {
                await Task.Delay(4000); // laisse le service démarrer
                await DownloadAllAsync();
            });

            _timer = new Timer(async _ => await DownloadAllAsync(), null,
                TimeSpan.FromHours(_refreshHours),
                TimeSpan.FromHours(_refreshHours));

            CoreLog.Info($"GoogleIpRange : démarré (refresh toutes les {_refreshHours}h)");
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public bool IsGoogleIp(string ip)
        {
            if (!IPAddress.TryParse(ip, out var addr)) return false;
            lock (_lock)
            {
                return _common.Any(r => r.Contains(addr))
                    || _special.Any(r => r.Contains(addr))
                    || _user.Any(r => r.Contains(addr));
            }
        }

        public bool IsCommonCrawlerIp(string ip)
        {
            if (!IPAddress.TryParse(ip, out var addr)) return false;
            lock (_lock) return _common.Any(r => r.Contains(addr));
        }

        private void TryLoadFromCache()
        {
            try
            {
                if (!File.Exists(CacheFile)) return;
                var json = File.ReadAllText(CacheFile);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var common  = ParseRangeList(root, "common");
                var special = ParseRangeList(root, "special");
                var user    = ParseRangeList(root, "user");
                var lastUpdate = File.GetLastWriteTimeUtc(CacheFile);

                lock (_lock)
                {
                    _common = common;
                    _special = special;
                    _user = user;
                    _lastUpdate = lastUpdate;
                    _isLoaded = true;
                }
                CoreLog.Info($"[GOOGLE-IP] Cache chargé : {_common.Count + _special.Count + _user.Count} plages " +
                             $"(maj {lastUpdate.ToLocalTime():dd/MM HH:mm})");
            }
            catch (Exception ex) { CoreLog.Warn($"[GOOGLE-IP] Chargement cache : {ex.Message}"); }
        }

        private async Task DownloadAllAsync()
        {
            if (!await _downloadLock.WaitAsync(0)) return;
            try
            {
                CoreLog.Info("[GOOGLE-IP] Téléchargement des listes officielles Google...");

                var common  = await DownloadAsync(CommonUrl);
                var special = await DownloadAsync(SpecialUrl);
                var user    = await DownloadAsync(UserUrl);

                int total = common.Count + special.Count + user.Count;
                if (total < 10)
                {
                    CoreLog.Warn($"[GOOGLE-IP] Parsing suspect : {total} plages au total");
                    return;
                }

                Directory.CreateDirectory(CacheDir);
                var cacheData = new
                {
                    common  = common.Select(c => c.ToString()).ToList(),
                    special = special.Select(c => c.ToString()).ToList(),
                    user    = user.Select(c => c.ToString()).ToList(),
                    updatedAt = DateTime.UtcNow
                };
                await File.WriteAllTextAsync(CacheFile, JsonSerializer.Serialize(cacheData));

                lock (_lock)
                {
                    _common = common;
                    _special = special;
                    _user = user;
                    _lastUpdate = DateTime.UtcNow;
                    _isLoaded = true;
                }
                CoreLog.Info($"[GOOGLE-IP] ✓ Téléchargement OK : " +
                             $"{common.Count} communs + {special.Count} spéciaux + {user.Count} user-triggered");
            }
            catch (Exception ex) { CoreLog.Warn($"[GOOGLE-IP] Échec téléchargement : {ex.Message}"); }
            finally { _downloadLock.Release(); }
        }

        private static async Task<List<IpCidr>> DownloadAsync(string url)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                string json = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);

                var result = new List<IpCidr>();
                if (!doc.RootElement.TryGetProperty("prefixes", out var prefixes)) return result;

                foreach (var p in prefixes.EnumerateArray())
                {
                    if (p.TryGetProperty("ipv4Prefix", out var v4))
                    {
                        string? cidr = v4.GetString();
                        if (!string.IsNullOrWhiteSpace(cidr) && TryParseCidr(cidr, out var parsed))
                            result.Add(parsed);
                    }
                    if (p.TryGetProperty("ipv6Prefix", out var v6))
                    {
                        string? cidr = v6.GetString();
                        if (!string.IsNullOrWhiteSpace(cidr) && TryParseCidr(cidr, out var parsed))
                            result.Add(parsed);
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"[GOOGLE-IP] Download '{url}' : {ex.Message}");
                return new List<IpCidr>();
            }
        }

        private static List<IpCidr> ParseRangeList(JsonElement root, string prop)
        {
            var result = new List<IpCidr>();
            if (!root.TryGetProperty(prop, out var arr)) return result;
            foreach (var item in arr.EnumerateArray())
            {
                string? s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s) && TryParseCidr(s, out var parsed))
                    result.Add(parsed);
            }
            return result;
        }

        private static bool TryParseCidr(string cidr, out IpCidr result)
        {
            result = default;
            var parts = cidr.Split('/');
            if (parts.Length != 2) return false;
            if (!IPAddress.TryParse(parts[0], out var net)) return false;
            if (!int.TryParse(parts[1], out int prefix)) return false;
            var bytes = net.GetAddressBytes();
            if (prefix < 0 || prefix > bytes.Length * 8) return false;
            result = new IpCidr(bytes, prefix);
            return true;
        }

        public void Dispose() => Stop();

        private readonly struct IpCidr
        {
            public readonly byte[] Network;
            public readonly int PrefixLength;

            public IpCidr(byte[] network, int prefix)
            {
                Network = network;
                PrefixLength = prefix;
            }

            public bool Contains(IPAddress addr)
            {
                var bytes = addr.GetAddressBytes();
                if (bytes.Length != Network.Length) return false;
                int fullBytes = PrefixLength / 8;
                int remBits = PrefixLength % 8;
                for (int i = 0; i < fullBytes; i++)
                    if (bytes[i] != Network[i]) return false;
                if (remBits > 0)
                {
                    int mask = 0xFF << (8 - remBits);
                    if ((bytes[fullBytes] & mask) != (Network[fullBytes] & mask)) return false;
                }
                return true;
            }

            public override string ToString() => $"{new IPAddress(Network)}/{PrefixLength}";
        }
    }
}