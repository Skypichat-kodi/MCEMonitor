using System;
using System.IO.Pipes;
using System.Linq;

namespace SystemMonitor.Service.Ipc
{
    public class IpcCommandHandler
    {
        private readonly SystemMonitorEngine _engine;
        private readonly SystemMonitorSettings _settings;

        public IpcCommandHandler(SystemMonitorEngine engine, SystemMonitorSettings settings)
        {
            _engine = engine;
            _settings = settings;
        }

        public void Handle(string command, NamedPipeServerStream server)
        {
            // Commandes avec arguments
            if (command.StartsWith("set-config ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetConfig(command, server);
                return;
            }

            if (command.StartsWith("set-web-config ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetWebConfig(command, server);
                return;
            }

            // Commandes exactes
            switch (command)
            {
                case "get-status":       HandleGetStatus(server); break;
                case "get-snapshot":     HandleGetSnapshot(server); break;
                case "get-cpu":          HandleGetCpu(server); break;
                case "get-gpus":         HandleGetGpus(server); break;
                case "get-ram":          HandleGetRam(server); break;
                case "get-networks":     HandleGetNetworks(server); break;
                case "get-config":       HandleGetConfig(server); break;
                case "get-web-status":   HandleGetWebStatus(server); break;
                case "force-scan":       HandleForceScan(server); break;
                case "shutdown":         HandleShutdown(server); break;
                case "get-alerts":       HandleGetAlerts(server); break;
                case "clear-alerts":     HandleClearAlerts(server); break;
                case "get-history":      HandleGetHistory(server); break;
                case "clear-history":    HandleClearHistory(server); break;
                case "get-bsods":        HandleGetBsods(server); break;
                case "clear-bsods":      HandleClearBsods(server); break;                                
                default:                 IpcResponse.Error(server, "unknown command"); break;
            }
        }

        // ============================================================
        //  STATUS / SNAPSHOT
        // ============================================================
        private void HandleGetStatus(NamedPipeServerStream server)
        {
            IpcResponse.Json(server, new
            {
                running = _engine.IsRunning,
                lastUpdate = _engine.LastUpdateTime,
                interval = _settings.Interval,
                cpuUsage = _engine.LastSnapshot.Cpu.UsagePercent,
                cpuTemp = _engine.LastSnapshot.Cpu.Temperature,
                ramUsage = _engine.LastSnapshot.Ram.UsagePercent,
                gpuCount = _engine.LastSnapshot.Gpus.Count,
                worstSeverity = _engine.WorstSeverity    // ? NOUVEAU
            });
        }

        private void HandleGetSnapshot(NamedPipeServerStream server)
        {
            var snap = _engine.LastSnapshot;

            IpcResponse.Json(server, new
            {
                timestamp = snap.Timestamp,
                cpu = snap.Cpu,
                gpus = snap.Gpus,
                ram = snap.Ram,
                networks = snap.Networks
            });
        }

        private void HandleGetCpu(NamedPipeServerStream server)
        {
            IpcResponse.Json(server, _engine.LastSnapshot.Cpu);
        }

        private void HandleGetGpus(NamedPipeServerStream server)
        {
            IpcResponse.Json(server, _engine.LastSnapshot.Gpus);
        }

        private void HandleGetRam(NamedPipeServerStream server)
        {
            IpcResponse.Json(server, _engine.LastSnapshot.Ram);
        }

        private void HandleGetNetworks(NamedPipeServerStream server)
        {
            IpcResponse.Json(server, _engine.LastSnapshot.Networks);
        }

        // ============================================================
        //  CONFIG
        // ============================================================
        private void HandleGetConfig(NamedPipeServerStream server)
        {
            IpcResponse.Json(server, new
            {
                interval = _settings.Interval,
                alertOnHighCpu = _settings.AlertOnHighCpu,
                cpuThresholdPercent = _settings.CpuThresholdPercent,
                cpuCooldownMinutes = _settings.CpuCooldownMinutes,
                alertOnHighRam = _settings.AlertOnHighRam,
                ramThresholdPercent = _settings.RamThresholdPercent,
                alertOnHighTemp = _settings.AlertOnHighTemp,
                tempThresholdCelsius = _settings.TempThresholdCelsius
            });
        }

        private void HandleSetConfig(string command, NamedPipeServerStream server)
        {
            try
            {
                string payload = command.Substring("set-config ".Length).Trim();
                var parts = payload.Split('&');

                foreach (var part in parts)
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length != 2) continue;

                    string key = kv[0].Trim().ToLower();
                    string val = kv[1].Trim();

                    switch (key)
                    {
                        case "interval":
                            if (int.TryParse(val, out int i)) _settings.Interval = i;
                            break;
                        case "alertcpu":
                            if (bool.TryParse(val, out bool ac)) _settings.AlertOnHighCpu = ac;
                            break;
                        case "cputhreshold":
                            if (int.TryParse(val, out int ct)) _settings.CpuThresholdPercent = ct;
                            break;
                        case "cpucooldown":
                            if (int.TryParse(val, out int cc)) _settings.CpuCooldownMinutes = cc;
                            break;
                        case "alertram":
                            if (bool.TryParse(val, out bool ar)) _settings.AlertOnHighRam = ar;
                            break;
                        case "ramthreshold":
                            if (int.TryParse(val, out int rt)) _settings.RamThresholdPercent = rt;
                            break;
                        case "alerttemp":
                            if (bool.TryParse(val, out bool at)) _settings.AlertOnHighTemp = at;
                            break;
                        case "tempthreshold":
                            if (int.TryParse(val, out int tt)) _settings.TempThresholdCelsius = tt;
                            break;
                    }
                }

                _settings.Save();
                CoreLog.Write("IPC : config SystemMonitor mise à jour");
                IpcResponse.Ok(server);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(server, ex.Message);
            }
        }

        // ============================================================
        //  WEB
        // ============================================================
        private void HandleGetWebStatus(NamedPipeServerStream server)
        {
            IpcResponse.Json(server, new
            {
                enabled = _settings.WebEnabled,
                port = _settings.WebPort,
                username = _settings.WebUsername
            });
        }

        private void HandleSetWebConfig(string command, NamedPipeServerStream server)
        {
            try
            {
                string payload = command.Substring("set-web-config ".Length).Trim();
                var parts = payload.Split('&');

                foreach (var part in parts)
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length != 2) continue;

                    string key = kv[0].Trim().ToLower();
                    string val = kv[1].Trim();

                    switch (key)
                    {
                        case "enabled":
                            if (bool.TryParse(val, out bool e)) _settings.WebEnabled = e;
                            break;
                        case "port":
                            if (int.TryParse(val, out int p)) _settings.WebPort = p;
                            break;
                        case "username":
                            _settings.WebUsername = val;
                            break;
                        case "password":
                            _settings.WebPassword = val;
                            break;
                    }
                }

                _settings.Save();

                // Redémarrer le WebServer
                Program.RestartWebServer();

                CoreLog.Write($"IPC : config WebServer mise à jour (enabled={_settings.WebEnabled}, port={_settings.WebPort})");
                IpcResponse.Ok(server);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur HandleSetWebConfig : " + ex.Message);
                IpcResponse.Error(server, ex.Message);
            }
        }

        // ============================================================
        //  ACTIONS
        // ============================================================
        private void HandleForceScan(NamedPipeServerStream server)
        {
            try
            {
                _engine.ForceTick();
                IpcResponse.Ok(server);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur HandleForceScan : " + ex.Message);
                IpcResponse.Error(server, ex.Message);
            }
        }

        private void HandleShutdown(NamedPipeServerStream server)
        {
            IpcResponse.Ok(server);
            CoreLog.Write("IPC : shutdown demandé");

            System.Threading.Tasks.Task.Delay(500).ContinueWith(_ => Environment.Exit(0));
        }
        
        private void HandleGetAlerts(NamedPipeServerStream server)
        {
            try
            {
                var alerts = _engine.GetAlerts();
                IpcResponse.Json(server, alerts);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(server, ex.Message);
            }
        }

        private void HandleClearAlerts(NamedPipeServerStream server)
        {
            try
            {
                _engine.ClearAlerts();
                CoreLog.Write("IPC : historique des alertes vidé");
                IpcResponse.Ok(server);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur HandleClearAlerts : " + ex.Message);
                IpcResponse.Error(server, ex.Message);
            }
        }
        
        private void HandleGetHistory(NamedPipeServerStream server)
        {
            try
            {
                var history = _engine.GetHistory();
                IpcResponse.Json(server, history);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(server, ex.Message);
            }
        }

        private void HandleClearHistory(NamedPipeServerStream server)
        {
            try
            {
                _engine.ClearHistory();
                CoreLog.Write("IPC : historique des mesures vidé");
                IpcResponse.Ok(server);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur HandleClearHistory : " + ex.Message);
                IpcResponse.Error(server, ex.Message);
            }
        }
        
        private void HandleGetBsods(NamedPipeServerStream server)
        {
            try
            {
                var bsods = _engine.GetBsods();
                IpcResponse.Json(server, bsods);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(server, ex.Message);
            }
        }

        private void HandleClearBsods(NamedPipeServerStream server)
        {
            try
            {
                _engine.ClearBsods();
                CoreLog.Write("IPC : historique BSOD vidé");
                IpcResponse.Ok(server);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur HandleClearBsods : " + ex.Message);
                IpcResponse.Error(server, ex.Message);
            }
        }                        
    }
}