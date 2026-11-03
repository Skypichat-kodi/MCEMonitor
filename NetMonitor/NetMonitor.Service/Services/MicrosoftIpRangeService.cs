using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NetMonitor.Service.Services
{
    /// <summary>
    /// Télécharge et maintient en cache la liste officielle des plages IP Microsoft
    /// (Azure + Office 365 + Teams + Skype...), publiée chaque semaine par Microsoft.
    /// Utilisée pour distinguer les vraies IP Microsoft des faux positifs.
    /// </summary>
    public class MicrosoftIpRangeService
    {
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "NetMonitor");

        private static readonly string CacheFile = Path.Combine(CacheDir, "ms-ip-ranges.json");

        private const string DownloadPageUrl =
            "https://www.microsoft.com/en-us/download/details.aspx?id=56519";

        private readonly object _lock = new();
        private List<IpCidr> _ranges = new();
        private DateTime _lastUpdate = DateTime.MinValue;
        private bool _isLoaded = false;

        private readonly bool _enabled;
        private readonly int _refreshHours;
        private Timer? _timer;
        private readonly SemaphoreSlim _downloadLock = new(1, 1);

        public bool IsEnabled => _enabled;
        public bool IsLoaded { get { lock (_lock) return _isLoaded; } }
        public DateTime LastUpdate { get { lock (_lock) return _lastUpdate; } }
        public int RangeCount { get { lock (_lock) return _ranges.Count; } }

        public MicrosoftIpRangeService(bool enabled, int refreshHours)
        {
            _enabled = enabled;
            _refreshHours = Math.Max(1, refreshHours);
        }

        public void Start()
        {
            if (!_enabled)
            {
                CoreLog.Info("MicrosoftIpRange : désactivé");
                return;
            }

            TryLoadFromCache();

            // Premier téléchargement différé (laisse le service finir de démarrer)
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                await DownloadAndParseAsync();
            });

            _timer = new Timer(async _ => await DownloadAndParseAsync(),
                              null,
                              TimeSpan.FromHours(_refreshHours),
                              TimeSpan.FromHours(_refreshHours));

            CoreLog.Info($"MicrosoftIpRange : démarré (refresh toutes les {_refreshHours}h)");
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public bool IsMicrosoftIp(string ip)
        {
            if (!IPAddress.TryParse(ip, out var addr)) return false;
            lock (_lock)
            {
                if (_ranges.Count == 0) return false;
                foreach (var r in _ranges)
                    if (r.Contains(addr)) return true;
                return false;
            }
        }

        private void TryLoadFromCache()
        {
            try
            {
                if (!File.Exists(CacheFile)) return;
                var json = File.ReadAllText(CacheFile);
                var ranges = ParseJson(json);
                var lastUpdate = File.GetLastWriteTimeUtc(CacheFile);

                lock (_lock) { _ranges = ranges; _lastUpdate = lastUpdate; _isLoaded = true; }
                CoreLog.Info($"[MS-IP] Cache chargé : {ranges.Count} plages " +
                             $"(maj {lastUpdate.ToLocalTime():dd/MM HH:mm})");
            }
            catch (Exception ex) { CoreLog.Warn($"[MS-IP] Chargement cache : {ex.Message}"); }
        }

        private async Task DownloadAndParseAsync()
        {
            if (!await _downloadLock.WaitAsync(0)) return;
            try
            {
                CoreLog.Info("[MS-IP] Téléchargement de la liste officielle Microsoft...");

                string? url = await FindDownloadUrlAsync();
                if (string.IsNullOrEmpty(url))
                {
                    CoreLog.Warn("[MS-IP] URL de téléchargement introuvable");
                    return;
                }

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                string json = await http.GetStringAsync(url);

                var ranges = ParseJson(json);
                if (ranges.Count < 100)
                {
                    CoreLog.Warn($"[MS-IP] Parsing suspect : {ranges.Count} plages (attendu >1000)");
                    return;
                }

                Directory.CreateDirectory(CacheDir);
                await File.WriteAllTextAsync(CacheFile, json);

                lock (_lock) { _ranges = ranges; _lastUpdate = DateTime.UtcNow; _isLoaded = true; }
                CoreLog.Info($"[MS-IP] ✓ Téléchargement OK : {ranges.Count} plages " +
                             $"(source: {url.Split('/').Last()})");
            }
            catch (Exception ex) { CoreLog.Warn($"[MS-IP] Échec téléchargement : {ex.Message}"); }
            finally { _downloadLock.Release(); }
        }

        private async Task<string?> FindDownloadUrlAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                string html = await http.GetStringAsync(DownloadPageUrl);

                // Pattern : https://download.microsoft.com/.../ServiceTags_Public_YYYYMMDD.json
                var regex = new Regex(
                    @"https://download\.microsoft\.com/download/[^""'\s<>]+ServiceTags_Public_\d{8}\.json",
                    RegexOptions.IgnoreCase);

                var urls = regex.Matches(html)
                    .Cast<Match>()
                    .Select(m => m.Value)
                    .Distinct()
                    .OrderByDescending(u => u) // YYYYMMDD → ordre lexicographique = chronologique
                    .ToList();

                if (urls.Count == 0)
                {
                    CoreLog.Warn("[MS-IP] Aucune URL ServiceTags dans la page");
                    return null;
                }

                CoreLog.Debug($"[MS-IP] {urls.Count} URL(s), retenu : {urls[0]}");
                return urls[0];
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"[MS-IP] Scraping échoué : {ex.Message}");
                return null;
            }
        }

        private List<IpCidr> ParseJson(string json)
        {
            var result = new List<IpCidr>();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("values", out var values)) return result;

            foreach (var service in values.EnumerateArray())
            {
                if (!service.TryGetProperty("properties", out var props)) continue;
                if (!props.TryGetProperty("addressPrefixes", out var addrs)) continue;

                foreach (var addr in addrs.EnumerateArray())
                {
                    string? cidr = addr.GetString();
                    if (!string.IsNullOrWhiteSpace(cidr) && TryParseCidr(cidr, out var parsed))
                        result.Add(parsed);
                }
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
        }
    }
}