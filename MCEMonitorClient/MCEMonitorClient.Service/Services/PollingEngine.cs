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
    /// <summary>
    /// Boucle de polling : interroge tous les serveurs à intervalle régulier
    /// et détecte les changements d'état.
    /// </summary>
    public class PollingEngine
    {
        private readonly Dictionary<string, PollResult> _lastResults = new();
        private readonly object _lock = new();

        private Thread? _thread;
        private bool _running;
        private DateTime _lastFullScan = DateTime.MinValue;

        // --- Tracking des médias pour éviter les faux started/stopped ---
        private readonly Dictionary<string, (DateTime LastSeen, MediaItem Media)> _mediaTrack = new();

        // Durée pendant laquelle un média absent est considéré comme "peut-être de retour"
        private const int MediaStopGraceSeconds = 90;
        
        // Événements pour notifier l'IPC
        public event Action<PollResult, PollResult?>? OnStateChanged;   // (nouveau, ancien)

        // --- État global ---
        public string GlobalState { get; private set; } = "ok";

        // --- Derniers résultats (pour IPC) ---
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

        // --- Démarrage ---
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

        /// <summary>
        /// Force un scan immédiat (via IPC).
        /// </summary>
        public void ForceScan()
        {
            CoreLog.Write("ForceScan demandé");
            _lastFullScan = DateTime.MinValue;
        }

        // --- Boucle principale ---
        private void Loop()
        {
            while (_running)
            {
                try
                {
                    var config = ServerConfigStore.Load();
                    int interval = Math.Max(5, config.PollIntervalSeconds);

                    // Force scan si demandé
                    bool forceScan = (DateTime.Now - _lastFullScan).TotalSeconds > interval;

                    if (forceScan)
                    {
                        _lastFullScan = DateTime.Now;
                        _ = PollAllAsync(config);
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

            foreach (var server in activeServers)
            {
                var result = await PollOneAsync(server);
                ProcessResult(result, config);
            }

            // Nettoyage : enlève les serveurs qui ne sont plus dans la config
            lock (_lock)
            {
                var validIds = config.Servers.Select(s => s.Id).ToHashSet();
                var toRemove = _lastResults.Keys.Where(id => !validIds.Contains(id)).ToList();
                foreach (var id in toRemove)
                    _lastResults.Remove(id);
            }
        }

        // --- Poll d'un serveur ---
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

                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
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

                // ? Parser les problèmes
                if (root.TryGetProperty("problems", out var problemsProp)
                    && problemsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in problemsProp.EnumerateArray())
                    {
                        var item = new ProblemItem();

                        if (p.TryGetProperty("severity", out var probSev))
                            item.Severity = probSev.GetString() ?? "";

                        if (p.TryGetProperty("category", out var probCat))
                            item.Category = probCat.GetString() ?? "";

                        if (p.TryGetProperty("message", out var probMsg))
                            item.Message = probMsg.GetString() ?? "";

                        result.Problems.Add(item);
                    }

                    result.ProblemCount = result.Problems.Count;

                    if (result.Problems.Count > 0)
                        result.FirstProblem = result.Problems[0].Message;
                }
                // ? Parser les médias en cours (pour MediaMonitor)
                if (root.TryGetProperty("media", out var mediaProp)
                    && mediaProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in mediaProp.EnumerateArray())
                    {
                        var item = new MediaItem();

                        if (m.TryGetProperty("client", out var cProp))
                            item.Client = cProp.GetString() ?? "";

                        if (m.TryGetProperty("type", out var tProp))
                            item.Type = tProp.GetString() ?? "";

                        if (m.TryGetProperty("title", out var tiProp))
                            item.Title = tiProp.GetString() ?? "";

                        if (m.TryGetProperty("saison", out var saisonProp) && saisonProp.TryGetInt32(out int saisonVal))
                            item.Saison = saisonVal;

                        if (m.TryGetProperty("episode", out var episodeProp) && episodeProp.TryGetInt32(out int episodeVal))
                            item.Episode = episodeVal;

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

        // --- Traitement du résultat ---
        private void ProcessResult(PollResult result, ServersConfig config)
        {
            PollResult? previous = null;
            bool stateChanged = false;

            lock (_lock)
            {
                if (_lastResults.TryGetValue(result.ServerId, out previous))
                    stateChanged = previous.Status != result.Status;

                _lastResults[result.ServerId] = result;
            }

            UpdateGlobalState();

            CoreLog.Write($"[POLL] {result.ServerName} : {result.Status}");

            // ? Changement d'état (problèmes)
            if (stateChanged && previous != null && config.NotifyOnStateChange)
            {
                OnStateChanged?.Invoke(result, previous);
                PushChannel.PushAlert(result, previous);
            }

            // ? Changement de médias (pour MediaMonitor)
            if (result.ServiceType == "MediaMonitor" && config.NotifyOnStateChange)
            {
                DetectMediaChanges(result, previous);
            }
        }

        private void DetectMediaChanges(PollResult current, PollResult? previous)
        {
            // Premier poll : on enregistre les médias sans rien notifier
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

            // ---------------------------------------------------------
            // 1) Médias présents : on met à jour le tracking,
            //    et on ne fire "started" QUE si vraiment nouveau
            // ---------------------------------------------------------
            foreach (var media in current.Media)
            {
                string fullKey = $"{current.ServerId}|{media.Key}";

                bool existedInPrevious = previous.Media.Any(m => m.Key == media.Key);
                bool existedBefore     = _mediaTrack.ContainsKey(fullKey);

                _mediaTrack[fullKey] = (now, media);

                // "started" uniquement si le média n'était ni dans le poll précédent,
                // ni connu dans notre tracking (donc jamais vu récemment)
                if (!existedInPrevious && !existedBefore)
                {
                    PushChannel.PushMediaEvent(current, media, "started");
                }
            }

            // ---------------------------------------------------------
            // 2) Médias absents : on attend le délai de grâce avant
            //    de considérer qu'ils sont vraiment terminés
            // ---------------------------------------------------------
            var toRemove = new List<string>();

            foreach (var kvp in _mediaTrack)
            {
                string fullKey = kvp.Key;

                if (!fullKey.StartsWith(current.ServerId + "|"))
                    continue;

                if (currKeys.Contains(fullKey))
                    continue;

                double secondsAbsent = (now - kvp.Value.LastSeen).TotalSeconds;

                if (secondsAbsent > MediaStopGraceSeconds)
                {
                    // Vraiment disparu ? on fire "stopped" avec le dernier état connu
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