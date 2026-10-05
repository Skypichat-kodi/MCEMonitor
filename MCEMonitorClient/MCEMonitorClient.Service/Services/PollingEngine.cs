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

                if (root.TryGetProperty("problems", out var problemsProp)
                    && problemsProp.ValueKind == JsonValueKind.Array)
                {
                    int count = 0;
                    foreach (var p in problemsProp.EnumerateArray())
                    {
                        count++;

                        if (count == 1 && p.TryGetProperty("message", out var msgProp))
                            result.FirstProblem = msgProp.GetString() ?? "";
                    }
                    result.ProblemCount = count;
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

            // Met à jour l'état global
            UpdateGlobalState();

            // Log systématique (utile pour debug)
            CoreLog.Write($"[POLL] {result.ServerName} : {result.Status}");

            // Si changement d'état ? notifier
            if (stateChanged && previous != null && config.NotifyOnStateChange)
            {
                OnStateChanged?.Invoke(result, previous);
            }
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