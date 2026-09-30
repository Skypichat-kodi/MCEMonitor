using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace MediaMonitor.Service.Web
{
    public class PeerStatus
    {
        public string Name       { get; set; } = "";
        public int    Port       { get; set; }
        public bool   Enabled    { get; set; }
        public bool   Online     { get; set; }
        public long   ResponseMs { get; set; }
    }

    public static class PeerStatusService
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor");

        private static readonly PeerDef[] AllPeers = new[]
        {
            new PeerDef("SystemMonitor", "SystemMonitor.config",
                new[] { "WebEnabled", "Enabled" },
                new[] { "WebPort",    "Port" },
                new[] { "WebUsername","Username" },
                new[] { "WebPassword","Password" }),

            new PeerDef("RomMonitor", "RomMonitor.config",
                new[] { "WebEnabled", "Enabled" },
                new[] { "WebPort",    "Port" },
                new[] { "WebUsername","Username" },
                new[] { "WebPassword","Password" }),

            new PeerDef("MediaMonitor", "MediaMonitor.Web.config",
                new[] { "Enabled", "WebEnabled" },
                new[] { "Port",    "WebPort" },
                new[] { "Username","WebUsername" },
                new[] { "Password","WebPassword" }),
        };

        public static async Task<List<PeerStatus>> CheckAllAsync(string selfName)
        {
            var results = new List<PeerStatus>();

            foreach (var def in AllPeers)
            {
                if (def.Name.Equals(selfName, StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add(await CheckOneAsync(def));
            }

            return results;
        }

        private static async Task<PeerStatus> CheckOneAsync(PeerDef def)
        {
            var status = new PeerStatus { Name = def.Name };

            try
            {
                string path = Path.Combine(ConfigDir, def.ConfigFile);
                if (!File.Exists(path))
                {
                    status.Enabled = false;
                    return status;
                }

                string? enabled = ReadKey(path, def.EnabledKeys);
                string? port    = ReadKey(path, def.PortKeys);
                string? user    = ReadKey(path, def.UserKeys);
                string? pass    = ReadKey(path, def.PassKeys);

                status.Enabled = string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase);

                if (!int.TryParse(port, out int portNum) || portNum <= 0)
                {
                    status.Enabled = false;
                    return status;
                }

                status.Port = portNum;

                if (!status.Enabled)
                    return status;

                using var handler = new HttpClientHandler
                {
                    Credentials = new NetworkCredential(user ?? "", pass ?? ""),
                    PreAuthenticate = true
                };

                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };

                var sw = Stopwatch.StartNew();
                var resp = await http.GetAsync($"http://127.0.0.1:{portNum}/ping");
                sw.Stop();

                status.Online = resp.IsSuccessStatusCode;
                status.ResponseMs = sw.ElapsedMilliseconds;
            }
            catch
            {
                status.Online = false;
            }

            return status;
        }

        private static string? ReadKey(string file, string[] keys)
        {
            try
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    foreach (var key in keys)
                    {
                        if (line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                            return line.Split('=', 2)[1].Trim();
                    }
                }
            }
            catch { }
            return null;
        }

        private sealed class PeerDef
        {
            public string Name { get; }
            public string ConfigFile { get; }
            public string[] EnabledKeys { get; }
            public string[] PortKeys { get; }
            public string[] UserKeys { get; }
            public string[] PassKeys { get; }

            public PeerDef(string name, string configFile,
                string[] enabledKeys, string[] portKeys, string[] userKeys, string[] passKeys)
            {
                Name = name;
                ConfigFile = configFile;
                EnabledKeys = enabledKeys;
                PortKeys = portKeys;
                UserKeys = userKeys;
                PassKeys = passKeys;
            }
        }
        // ============================================================
        //  Cache en arrière-plan
        // ============================================================
        private static List<PeerStatus> _cachedPeers = new();
        private static readonly object _cacheLock = new();
        private static System.Threading.Timer? _refreshTimer;
        private static string _selfName = "";

        public static void StartBackgroundRefresh(string selfName)
        {
            _selfName = selfName;

            // Premier refresh immédiat
            _ = RefreshCacheAsync();

            // Puis toutes les 10 s
            _refreshTimer = new System.Threading.Timer(
                _ => _ = RefreshCacheAsync(),
                null,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(10));
        }

        private static async Task RefreshCacheAsync()
        {
            try
            {
                var peers = await CheckAllAsync(_selfName);
                lock (_cacheLock)
                {
                    _cachedPeers = peers;
                }
            }
            catch { /* silencieux */ }
        }

        public static List<PeerStatus> GetCachedPeers()
        {
            lock (_cacheLock)
            {
                return new List<PeerStatus>(_cachedPeers);
            }
        }

        public static string BuildPeerBarHtml(string publicHost)
        {
            var peers = GetCachedPeers();
            var sb = new System.Text.StringBuilder();

            if (string.IsNullOrWhiteSpace(publicHost))
                publicHost = "localhost";

            foreach (var p in peers)
            {
                bool isDisabled = !p.Enabled;
                bool isOnline   = p.Enabled && p.Online;

                string dotClass = isDisabled ? "disabled" : (isOnline ? "online" : "offline");
                string extraBtn = isDisabled ? "disabled" : (isOnline ? "" : "offline");

                string tooltip = isDisabled
                    ? $"{p.Name} — désactivé"
                    : isOnline
                        ? $"{p.Name} — en ligne ({p.ResponseMs} ms)"
                        : $"{p.Name} — hors ligne";

                string inner = $@"<span class=""peer-dot {dotClass}""></span><span class=""peer-name"">{p.Name}</span>";

                if (isOnline)
                {
                    string href = $"http://{publicHost}:{p.Port}/";
                    string targetName = "mm_" + p.Name.Replace(" ", "_");

                    sb.Append($@"<a class=""peer-btn"" href=""{href}"" target=""{targetName}"" title=""{tooltip}"">{inner}</a>");
                }
                else
                {
                    sb.Append($@"<button class=""peer-btn {extraBtn}"" title=""{tooltip}"" disabled type=""button"">{inner}</button>");
                }
            }

            return sb.ToString();
        }
    }
}