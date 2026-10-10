using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCEMonitorClient.Shared.Models;

namespace MCEMonitorClient.Shared.Services
{
    public class PollingEngine
    {
        private readonly Dictionary<string, PollResult> _lastResults = new();
        private readonly object _lock = new();

        // --- Tracking des médias pour éviter les faux started/stopped ---
        private readonly Dictionary<string, (DateTime LastSeen, MediaItem Media)> _mediaTrack = new();

        // Durée pendant laquelle un média absent est considéré comme "peut-être de retour"
        private const int MediaStopGraceSeconds = 90;

        // Événements pour notifier l'app
        public event Action<PollResult, PollResult?>? OnStateChanged;
        public event Action<PollResult, MediaItem, string>? OnMediaEvent;

        public string GlobalState { get; private set; } = "ok";

        public List<PollResult> GetSnapshot()
        {
            lock (_lock)
                return _lastResults.Values.ToList();
        }

        /// <summary>
        /// Effectue un scan complet de tous les serveurs actifs.
        /// À appeler périodiquement (depuis un Foreground Service par ex).
        /// </summary>
        public async Task PollAllAsync(ServersConfig config, CancellationToken ct)
        {
            var activeServers = config.Servers.Where(s => s.Enabled).ToList();

            foreach (var server in activeServers)
            {
                ct.ThrowIfCancellationRequested();

                var result = await PollOneAsync(server);
                ProcessResult(result, config);
            }

            // Nettoyage : retire les serveurs qui ne sont plus dans la config
            lock (_lock)
            {
                var validIds = config.Servers.Select(s => s.Id).ToHashSet();
                var toRemove = _lastResults.Keys.Where(id => !validIds.Contains(id)).ToList();
                foreach (var id in toRemove)
                    _lastResults.Remove(id);
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
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

                // Auth Basic manuelle (Credentials/PreAuthenticate ne marchent pas sur Android)
                if (!string.IsNullOrEmpty(server.Username))
                {
                    string raw = $"{server.Username}:{server.Password}";
                    string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
                    http.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", b64);
                }

                using var resp = await http.GetAsync(server.ApiSummaryUrl);

                // Log le code HTTP pour diagnostic
                System.Diagnostics.Trace.WriteLine(
                    $"[POLL] {server.Name} -> {(int)resp.StatusCode} {resp.StatusCode}");

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

                // Problèmes
                if (root.TryGetProperty("problems", out var problemsProp)
                    && problemsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in problemsProp.EnumerateArray())
                    {
                        var item = new ProblemItem();
                        if (p.TryGetProperty("severity", out var s)) item.Severity = s.GetString() ?? "";
                        if (p.TryGetProperty("category", out var c)) item.Category = c.GetString() ?? "";
                        if (p.TryGetProperty("message", out var m))  item.Message  = m.GetString() ?? "";
                        result.Problems.Add(item);
                    }

                    result.ProblemCount = result.Problems.Count;
                    if (result.Problems.Count > 0)
                        result.FirstProblem = result.Problems[0].Message;
                }

                // Médias
                if (root.TryGetProperty("media", out var mediaProp)
                    && mediaProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in mediaProp.EnumerateArray())
                    {
                        var item = new MediaItem();
                        if (m.TryGetProperty("client", out var c))  item.Client = c.GetString() ?? "";
                        if (m.TryGetProperty("type", out var t))    item.Type   = t.GetString() ?? "";
                        if (m.TryGetProperty("title", out var ti))  item.Title  = ti.GetString() ?? "";
                        if (m.TryGetProperty("saison", out var sa) && sa.TryGetInt32(out int saisonVal))
                            item.Saison = saisonVal;
                        if (m.TryGetProperty("episode", out var ep) && ep.TryGetInt32(out int episodeVal))
                            item.Episode = episodeVal;
                        result.Media.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[POLL] {server.Name} EXCEPTION: {ex.Message}");
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

                _lastResults[result.ServerId] = result;
            }

            UpdateGlobalState();

            // Changement d'état (problèmes)
            if (stateChanged && previous != null && config.NotifyOnStateChange)
            {
                OnStateChanged?.Invoke(result, previous);
            }

            // Changement de médias
            if (result.ServiceType == "MediaMonitor" && config.NotifyOnStateChange)
            {
                DetectMediaChanges(result, previous);
            }
        }

        private void DetectMediaChanges(PollResult current, PollResult? previous)
        {
            // Premier poll pour ce serveur : on enregistre sans rien notifier
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
            // 1) Médias présents : mise à jour du tracking
            //    "started" uniquement si jamais vu récemment
            // ---------------------------------------------------------
            foreach (var media in current.Media)
            {
                string fullKey = $"{current.ServerId}|{media.Key}";

                bool existedInPrevious = previous.Media.Any(m => m.Key == media.Key);
                bool existedBefore     = _mediaTrack.ContainsKey(fullKey);

                _mediaTrack[fullKey] = (now, media);

                if (!existedInPrevious && !existedBefore)
                {
                    OnMediaEvent?.Invoke(current, media, "started");
                }
            }

            // ---------------------------------------------------------
            // 2) Médias absents : on attend le délai de grâce
            //    avant de considérer qu'ils sont vraiment terminés
            // ---------------------------------------------------------
            var toRemove = new List<string>();

            foreach (var kvp in _mediaTrack)
            {
                string fullKey = kvp.Key;

                // On ne traite que les médias de ce serveur
                if (!fullKey.StartsWith(current.ServerId + "|"))
                    continue;

                if (currKeys.Contains(fullKey))
                    continue;

                double secondsAbsent = (now - kvp.Value.LastSeen).TotalSeconds;

                if (secondsAbsent > MediaStopGraceSeconds)
                {
                    OnMediaEvent?.Invoke(current, kvp.Value.Media, "stopped");
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