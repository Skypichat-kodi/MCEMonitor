using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCEMonitorClient.Service.Logs;
using MCEMonitorClient.Service.Models;
using MCEMonitorClient.Service.Ipc;

namespace MCEMonitorClient.Service.Services
{
    public class PollingEngine
    {
        private readonly Dictionary<string, PollResult> _lastResults = new();
        private readonly object _lock = new();

        private Thread? _thread;
        private bool _running;
        private DateTime _lastFullScan = DateTime.MinValue;

        // --- Tracking des médias pour éviter les faux started/stopped ---
        private readonly Dictionary<string, (DateTime LastSeen, MediaItem Media)> _mediaTrack = new();
        
        // --- Anti-faux-positif : compteurs ---
        private readonly Dictionary<string, int> _consecutiveFailures = new();
        private readonly Dictionary<string, int> _consecutiveSuccesses = new();
        
        // Paramètres ajustés pour éviter le "flapping"
        private const int OfflineThreshold = 3;    // 3 échecs consécutifs avant de dire "offline"
        private const int RecoveryThreshold = 2;   // 2 succès consécutifs avant de dire "ok"
        private const int HttpTimeoutSeconds = 10; // Timeout HTTP plus patient (10s)

        // Durée pendant laquelle un média absent est considéré comme "peut-être de retour"
        private const int MediaStopGraceSeconds = 90;
        
        public event Action<PollResult, PollResult?>? OnStateChanged;   
        public string GlobalState { get; private set; } = "ok";

        public List<PollResult> GetSnapshot()
        {
            lock (_lock)
                return _lastResults.Values.ToList();
        }

        public PollResult? GetServerResult(string serverId)
        {
            lock (_lock)
                return _lastResults.TryGetValue(serverId, out var r) ? r : null;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
            CoreLog.Write("PollingEngine démarré");
        }

        public void Stop()
        {
            _running = false;
            CoreLog.Write("PollingEngine arrêté");
        }

        public void ForceScan()
        {
            CoreLog.Write("ForceScan demandé");
            _lastFullScan = DateTime.MinValue;
        }

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    var config = ServerConfigStore.Load();
                    int interval = Math.Max(5, config.PollIntervalSeconds);

                    bool forceScan = (DateTime.Now - _lastFullScan).TotalSeconds > interval;

                    if (forceScan)
                    {
                        _lastFullScan = DateTime.Now;
                        _ = PollAllAsync(config); // Fire & forget, le scan est parallélisé
                    }

                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    CoreLog.Write("PollingEngine Loop ERROR : " + ex.Message);
                    Thread.Sleep(5000);
                }
            }
        }

        private async Task PollAllAsync(ServersConfig config)
        {
            var activeServers = config.Servers.Where(s => s.Enabled).ToList();

            // --- NOUVEAU : Parallélisation des requêtes ---
            // On lance toutes les requêtes en même temps pour ne pas être bloqué par un serveur lent
            var tasks = activeServers.Select(async server =>
            {
                var result = await PollOneAsync(server);
                ProcessResult(result, config);
            });

            await Task.WhenAll(tasks);

            // Nettoyage
            lock (_lock)
            {
                var validIds = config.Servers.Select(s => s.Id).ToHashSet();
                var toRemove = _lastResults.Keys.Where(id => !validIds.Contains(id)).ToList();
                foreach (var id in toRemove)
                {
                    _lastResults.Remove(id);
                    _consecutiveFailures.Remove(id);
                    _consecutiveSuccesses.Remove(id);
                }
            }
        }

        private async Task<PollResult> PollOneAsync(ServerEntry server)
        {
            var result = new PollResult
            {
                ServerId = server.Id,
                ServerName = server.Name,
                ServiceType = server.ServiceType,
                BaseUrl = server.FullUrl,
                Timestamp = DateTime.Now
            };

            if (string.IsNullOrWhiteSpace(server.ApiSummaryUrl))
            {
                result.Online = false;
                result.Status = "offline";
                return result;
            }

            try
            {
                var handler = new HttpClientHandler
                {
                    Credentials = new NetworkCredential(server.Username, server.Password),
                    PreAuthenticate = true
                };

                // Timeout allongé à 10s
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds) };
                using var resp = await http.GetAsync(server.ApiSummaryUrl);

                if (!resp.IsSuccessStatusCode)
                {
                    result.Online = false;
                    result.Status = "offline";
                    return result;
                }

                string json = await resp.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                result.Online = true;

                if (root.TryGetProperty("status", out var statusProp))
                    result.Status = statusProp.GetString() ?? "ok";

                if (root.TryGetProperty("worstSeverity", out var sevProp))
                    result.WorstSeverity = sevProp.GetString() ?? "ok";

                if (root.TryGetProperty("problems", out var problemsProp)
                    && problemsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in problemsProp.EnumerateArray())
                    {
                        var item = new ProblemItem();
                        if (p.TryGetProperty("severity", out var probSev)) item.Severity = probSev.GetString() ?? "";
                        if (p.TryGetProperty("category", out var probCat)) item.Category = probCat.GetString() ?? "";
                        if (p.TryGetProperty("message", out var probMsg)) item.Message = probMsg.GetString() ?? "";
                        result.Problems.Add(item);
                    }
                    result.ProblemCount = result.Problems.Count;
                    if (result.Problems.Count > 0) result.FirstProblem = result.Problems[0].Message;
                }

                if (root.TryGetProperty("media", out var mediaProp)
                    && mediaProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in mediaProp.EnumerateArray())
                    {
                        var item = new MediaItem();
                        if (m.TryGetProperty("client", out var cProp)) item.Client = cProp.GetString() ?? "";
                        if (m.TryGetProperty("type", out var tProp)) item.Type = tProp.GetString() ?? "";
                        if (m.TryGetProperty("title", out var tiProp)) item.Title = tiProp.GetString() ?? "";
                        if (m.TryGetProperty("saison", out var saisonProp) && saisonProp.TryGetInt32(out int saisonVal)) item.Saison = saisonVal;
                        if (m.TryGetProperty("episode", out var episodeProp) && episodeProp.TryGetInt32(out int episodeVal)) item.Episode = episodeVal;
                        result.Media.Add(item);
                    }
                }
            }
            catch
            {
                result.Online = false;
                result.Status = "offline";
            }

            return result;
        }

        private void ProcessResult(PollResult result, ServersConfig config)
        {
            PollResult? previous = null;
            bool stateChanged = false;

            lock (_lock)
            {
                if (_lastResults.TryGetValue(result.ServerId, out previous))
                    stateChanged = previous.Status != result.Status;

                // ----- Hystérésis : Anti-faux-positif "offline" -----
                if (!result.Online || result.Status == "offline")
                {
                    _consecutiveFailures.TryGetValue(result.ServerId, out int fails);
                    fails++;
                    _consecutiveFailures[result.ServerId] = fails;
                    _consecutiveSuccesses[result.ServerId] = 0; // Reset succès

                    // Pas encore assez d'échecs ? on garde l'ancien état
                    if (fails < OfflineThreshold && previous != null)
                    {
                        result.Status = previous.Status;
                        result.Online = previous.Online;
                        stateChanged = false;
                    }
                }
                else
                {
                    _consecutiveSuccesses.TryGetValue(result.ServerId, out int succ);
                    succ++;
                    _consecutiveSuccesses[result.ServerId] = succ;
                    _consecutiveFailures[result.ServerId] = 0; // Reset échecs

                    // Si on était offline, il faut 2 succès consécutifs pour revenir à "ok"
                    if (previous != null && previous.Status == "offline" && succ < RecoveryThreshold)
                    {
                        result.Status = "offline";
                        result.Online = false;
                        stateChanged = false;
                    }
                }

                _lastResults[result.ServerId] = result;
            }

            UpdateGlobalState();

            CoreLog.Write($"[POLL] {result.ServerName} : {result.Status}");

            if (stateChanged && previous != null && config.NotifyOnStateChange)
            {
                OnStateChanged?.Invoke(result, previous);
                PushChannel.PushAlert(result, previous);
            }

            if (result.ServiceType == "MediaMonitor" && config.NotifyOnStateChange)
            {
                DetectMediaChanges(result, previous);
            }
        }

        private void DetectMediaChanges(PollResult current, PollResult? previous)
        {
            if (previous == null)
            {
                foreach (var media in current.Media)
                {
                    string fullKey = $"{current.ServerId}|{media.Key}";
                    _mediaTrack[fullKey] = (DateTime.Now, media);
                }
                return;
            }

            var now = DateTime.Now;
            var currKeys = current.Media.Select(m => $"{current.ServerId}|{m.Key}").ToHashSet();

            foreach (var media in current.Media)
            {
                string fullKey = $"{current.ServerId}|{media.Key}";
                bool existedInPrevious = previous.Media.Any(m => m.Key == media.Key);
                bool existedBefore     = _mediaTrack.ContainsKey(fullKey);

                _mediaTrack[fullKey] = (now, media);

                if (!existedInPrevious && !existedBefore)
                {
                    PushChannel.PushMediaEvent(current, media, "started");
                }
            }

            var toRemove = new List<string>();
            foreach (var kvp in _mediaTrack)
            {
                string fullKey = kvp.Key;
                if (!fullKey.StartsWith(current.ServerId + "|")) continue;
                if (currKeys.Contains(fullKey)) continue;

                double secondsAbsent = (now - kvp.Value.LastSeen).TotalSeconds;
                if (secondsAbsent > MediaStopGraceSeconds)
                {
                    PushChannel.PushMediaEvent(current, kvp.Value.Media, "stopped");
                    toRemove.Add(fullKey);
                }
            }

            foreach (var k in toRemove)
                _mediaTrack.Remove(k);
        }

        private void UpdateGlobalState()
        {
            lock (_lock)
            {
                string state = "ok";
                foreach (var r in _lastResults.Values)
                {
                    if (r.Status == "critical") { state = "critical"; break; }
                    if (r.Status == "warning" && state == "ok") state = "warning";
                    if (r.Status == "offline" && state == "ok") state = "offline";
                }
                GlobalState = state;
            }
        }
    }
}