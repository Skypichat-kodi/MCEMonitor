using System;
using System.IO.Pipes;
using System.Linq;
using RomMonitor.Service.Ipc;

namespace RomMonitor.Service
{
    public class IpcCommandHandler
    {
        private readonly RomMonitorEngine _engine;
        private readonly RomMonitorSettings _settings;

        public IpcCommandHandler(RomMonitorEngine engine, RomMonitorSettings settings)
        {
            _engine = engine;
            _settings = settings;
        }

        public void Handle(string command, NamedPipeServerStream server)
        {
            // Commandes avec arguments (StartsWith)
            if (command.StartsWith("set-config ", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetConfig(command, server);
                return;
            }

            // Commandes exactes
            switch (command)
            {
                case "get-status":       HandleGetStatus(server); break;
                case "get-disks":        HandleGetDisks(server); break;
                case "get-smart":        HandleGetSmart(server); break;
                case "get-alerts":       HandleGetAlerts(server); break;
                case "get-config":       HandleGetConfig(server); break;
                case "send-test-email":  HandleSendTestEmail(server); break;
                case "shutdown":         HandleShutdown(server); break;
                default:                 IpcResponse.Error(server, "unknown command"); break;
            }
        }

        private void HandleGetStatus(NamedPipeServerStream server)
        {
            IpcResponse.Json(server, new
            {
                lastCheck = _engine.LastCheckTime,
                diskCount = _engine.LastDisks.Count,
                smartCount = _engine.LastSmart.Count,
                alertCount = _engine.LastAlerts.Count,
                interval = _settings.Interval,
                alertOnSmartFailure = _settings.AlertOnSmartFailure
            });
        }

        private void HandleGetDisks(NamedPipeServerStream server)
        {
            var disks = _engine.LastDisks.Select(d => new
            {
                name = d.Name,
                label = d.Label,
                driveType = d.DriveType,
                totalGo = Math.Round(d.TotalGo, 1),
                freeGo = Math.Round(d.FreeGo, 1),
                freePercent = Math.Round(d.FreePercent, 1),
                physicalSerial = d.PhysicalSerial,           // ??
                physicalDiskNumber = d.PhysicalDiskNumber    // ??
            }).ToList();

            IpcResponse.Json(server, disks);
        }

        private void HandleGetSmart(NamedPipeServerStream server)
        {
            var smart = _engine.LastSmart.Select(s => new
            {
                device = s.Device,
                model = s.Model,
                serial = s.Serial,
                type = s.Type,
                status = s.Status,
                statusReason = s.StatusReason,
                temperature = s.Temperature,
                powerOnHours = s.PowerOnHours,
                passed = s.Passed
            }).ToList();

            IpcResponse.Json(server, smart);
        }

        private void HandleGetAlerts(NamedPipeServerStream server)
        {
            var alerts = _engine.LastAlerts.Select(a => new
            {
                timestamp = a.Timestamp,
                type = a.Type.ToString(),
                severity = a.Severity,
                target = a.Target,
                message = a.Message,
                emailSent = a.EmailSent
            }).ToList();

            IpcResponse.Json(server, alerts);
        }

        private void HandleGetConfig(NamedPipeServerStream server)
        {
            IpcResponse.Json(server, new
            {
                interval = _settings.Interval,
                diskSpaceWarnPercent = _settings.DiskSpaceWarnPercent,
                diskSpaceCriticalPercent = _settings.DiskSpaceCriticalPercent,
                diskSpaceWarnGo = _settings.DiskSpaceWarnGo,
                diskSpaceCriticalGo = _settings.DiskSpaceCriticalGo,
                alertOnSmartFailure = _settings.AlertOnSmartFailure,
                alertOnLowDiskSpace = _settings.AlertOnLowDiskSpace,
                alertCooldownHours = _settings.AlertCooldownHours
            });
        }

        private void HandleSendTestEmail(NamedPipeServerStream server)
        {
            try
            {
                _engine.SendTestEmailAsync().GetAwaiter().GetResult();
                IpcResponse.Ok(server);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(server, ex.Message);
            }
        }

        private void HandleShutdown(NamedPipeServerStream server)
        {
            IpcResponse.Ok(server);
            CoreLog.Write("IPC : shutdown demandé");

            System.Threading.Tasks.Task.Delay(500).ContinueWith(_ => Environment.Exit(0));
        }
        
        private void HandleSetConfig(string command, NamedPipeServerStream server)
        {
            try
            {
                // Récupérer la partie après "set-config "
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
                        case "warnpct":
                            if (int.TryParse(val, out int wp)) _settings.DiskSpaceWarnPercent = wp;
                            break;
                        case "critpct":
                            if (int.TryParse(val, out int cp)) _settings.DiskSpaceCriticalPercent = cp;
                            break;
                        case "warngo":
                            if (int.TryParse(val, out int wg)) _settings.DiskSpaceWarnGo = wg;
                            break;
                        case "critgo":
                            if (int.TryParse(val, out int cg)) _settings.DiskSpaceCriticalGo = cg;
                            break;
                        case "smart":
                            if (bool.TryParse(val, out bool s)) _settings.AlertOnSmartFailure = s;
                            break;
                        case "lowdisk":
                            if (bool.TryParse(val, out bool ld)) _settings.AlertOnLowDiskSpace = ld;
                            break;
                        case "cooldown":
                            if (int.TryParse(val, out int cd)) _settings.AlertCooldownHours = cd;
                            break;
                    }
                }

                _settings.Save();
                CoreLog.Write("IPC : config RomMonitor mise à jour");
                IpcResponse.Ok(server);
            }
            catch (Exception ex)
            {
                IpcResponse.Error(server, ex.Message);
            }
        }       
    }
}