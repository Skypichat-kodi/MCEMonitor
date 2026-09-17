using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RomMonitor.Service
{
    /// <summary>
    /// Interroge le SMART via smartctl.exe.
    /// Supporte NVMe, ATA (HDD/SSD SATA), SCSI.
    /// </summary>
    public static class DiskHealthChecker
    {
        private static string _smartctlPath = "";
        private const int SmartctlTimeoutMs = 15000;   // 15 secondes par disque

        // ------------------------------------------------------------------
        //  Chemin de smartctl.exe
        // ------------------------------------------------------------------
        private static string GetSmartctlPath()
        {
            if (!string.IsNullOrEmpty(_smartctlPath))
                return _smartctlPath;

            string path = Path.Combine(AppContext.BaseDirectory, "Tools", "smartctl.exe");

            if (File.Exists(path))
                _smartctlPath = path;

            return _smartctlPath;
        }

        // ------------------------------------------------------------------
        //  Point d'entrée : récupère les infos SMART de tous les disques
        // ------------------------------------------------------------------
        public static Dictionary<string, SmartInfo> GetAllSmartInfo()
        {
            var result = new Dictionary<string, SmartInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string smartctl = GetSmartctlPath();

                if (string.IsNullOrEmpty(smartctl))
                {
                    CoreLog.Write("SMART : smartctl.exe introuvable dans Tools\\");
                    return result;
                }

                // 1. Scanner les devices
                var devices = ScanDevices(smartctl);

                if (devices.Count == 0)
                {
                    CoreLog.Write("SMART : aucun device détecté");
                    return result;
                }

                // 2. Lire le SMART de chaque device (silencieux si non supporté)
                foreach (var device in devices)
                {
                    try
                    {
                        var info = ReadSmart(smartctl, device);

                        if (info != null && info.Available)
                        {
                            string key = !string.IsNullOrEmpty(info.Serial)
                                ? info.Serial
                                : device;

                            result[key] = info;

                            CoreLog.Write($"SMART : {device} - {info.Model} | S/N {info.Serial} | {info.Status}");
                        }
                    }
                    catch (Exception ex)
                    {
                        CoreLog.Write($"SMART : erreur sur {device} : {ex.Message}");
                    }
                }

                // 3. Résumé
                CoreLog.Write($"SMART : {result.Count} disque(s) avec SMART sur {devices.Count} device(s)");
            }
            catch (Exception ex)
            {
                CoreLog.Write("SMART : erreur globale : " + ex.Message);
            }

            return result;
        }

        // ------------------------------------------------------------------
        //  Scan des devices (avec timeout)
        // ------------------------------------------------------------------
        private static List<string> ScanDevices(string smartctl)
        {
            var result = new List<string>();

            var psi = new ProcessStartInfo
            {
                FileName = smartctl,
                Arguments = "--scan --json=o",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var p = Process.Start(psi);
            if (p == null) return result;

            string json = p.StandardOutput.ReadToEnd();

            if (!p.WaitForExit(SmartctlTimeoutMs))
            {
                try { p.Kill(true); } catch { }
                CoreLog.Write("SMART : timeout sur --scan");
                return result;
            }

            if (string.IsNullOrWhiteSpace(json))
                return result;

            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("devices", out var devices))
            {
                foreach (var dev in devices.EnumerateArray())
                {
                    if (dev.TryGetProperty("name", out var nameEl))
                    {
                        string name = nameEl.GetString() ?? "";
                        if (!string.IsNullOrEmpty(name))
                            result.Add(name);
                    }
                }
            }

            return result;
        }

        // ------------------------------------------------------------------
        //  Lecture SMART d'un device (avec timeout + parsing complet)
        // ------------------------------------------------------------------
        private static SmartInfo? ReadSmart(string smartctl, string device)
        {
            var psi = new ProcessStartInfo
            {
                FileName = smartctl,
                Arguments = $"-a --json=o {device}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var p = Process.Start(psi);
            if (p == null) return null;

            string json = p.StandardOutput.ReadToEnd();

            if (!p.WaitForExit(SmartctlTimeoutMs))
            {
                try { p.Kill(true); } catch { }
                CoreLog.Write($"SMART : timeout sur {device}");
                return null;
            }

            if (string.IsNullOrWhiteSpace(json))
                return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var info = new SmartInfo
            {
                Device = device,
                Available = false,
                Status = "N/A"
            };

            // --- Type de disque ---
            if (root.TryGetProperty("device", out var devEl) &&
                devEl.TryGetProperty("type", out var typeEl))
            {
                info.Type = typeEl.GetString() ?? "";
            }

            // --- Identité ---
            if (root.TryGetProperty("model_name", out var modelEl))
                info.Model = modelEl.GetString() ?? "";

            if (root.TryGetProperty("serial_number", out var serialEl))
                info.Serial = serialEl.GetString() ?? "";

            if (root.TryGetProperty("firmware_version", out var fwEl))
                info.Firmware = fwEl.GetString() ?? "";

            // --- Support SMART ? ---
            if (root.TryGetProperty("smart_support", out var supEl) &&
                supEl.TryGetProperty("available", out var availEl))
            {
                info.Available = availEl.GetBoolean();
            }

            if (!info.Available)
                return info;

            // --- Statut global ---
            if (root.TryGetProperty("smart_status", out var statusEl) &&
                statusEl.TryGetProperty("passed", out var passedEl))
            {
                info.Passed = passedEl.GetBoolean();
            }

            // --- Température ---
            if (root.TryGetProperty("temperature", out var tempEl) &&
                tempEl.TryGetProperty("current", out var tempCurrentEl))
            {
                info.Temperature = tempCurrentEl.GetInt32();
            }

            // --- Heures de fonctionnement ---
            if (root.TryGetProperty("power_on_time", out var potEl) &&
                potEl.TryGetProperty("hours", out var hoursEl))
            {
                info.PowerOnHours = hoursEl.GetInt32();
            }

            // --- Cas NVMe ---
            if (root.TryGetProperty("nvme_smart_health_information_log", out var nvmeEl))
            {
                if (nvmeEl.TryGetProperty("critical_warning", out var cwEl))
                    info.CriticalWarning = cwEl.GetInt32();

                if (nvmeEl.TryGetProperty("percentage_used", out var puEl))
                    info.PercentageUsed = puEl.GetInt32();

                if (nvmeEl.TryGetProperty("available_spare", out var asEl))
                    info.AvailableSpare = asEl.GetInt32();

                if (nvmeEl.TryGetProperty("media_errors", out var meEl))
                    info.MediaErrors = meEl.GetInt64();

                if (info.Temperature == null &&
                    nvmeEl.TryGetProperty("temperature", out var nvmeTempEl))
                {
                    info.Temperature = nvmeTempEl.GetInt32();
                }
            }

            // --- Cas ATA (HDD / SSD SATA) ---
            if (root.TryGetProperty("ata_smart_attributes", out var ataEl) &&
                ataEl.TryGetProperty("table", out var tableEl))
            {
                foreach (var attr in tableEl.EnumerateArray())
                {
                    if (!attr.TryGetProperty("id", out var idEl)) continue;
                    if (!attr.TryGetProperty("raw", out var rawEl)) continue;
                    if (!rawEl.TryGetProperty("value", out var valEl)) continue;

                    long value = valEl.GetInt64();
                    int id = idEl.GetInt32();

                    switch (id)
                    {
                        case 5:   info.ReallocatedSectors = value; break;
                        case 10:  info.SpinRetryCount = value; break;
                        case 197: info.PendingSectors = value; break;
                        case 198: info.UncorrectableSectors = value; break;
                    }
                }
            }

            // --- Calcul du statut ---
            info.Status = "OK";
            info.StatusReason = "";

            if (!info.Passed)
            {
                info.Status = "Critical";
                info.StatusReason = "SMART global : FAILED";
            }

            if (info.CriticalWarning.HasValue && info.CriticalWarning.Value != 0)
            {
                info.Status = "Critical";
                info.StatusReason = $"Critical Warning NVMe : 0x{info.CriticalWarning.Value:X2}";
            }

            if (info.ReallocatedSectors.HasValue && info.ReallocatedSectors.Value > 0)
            {
                info.Status = "Warning";
                info.StatusReason = $"{info.ReallocatedSectors.Value} secteurs réalloués";
            }

            if (info.PendingSectors.HasValue && info.PendingSectors.Value > 0)
            {
                info.Status = "Critical";
                info.StatusReason = $"{info.PendingSectors.Value} secteurs en attente";
            }

            if (info.UncorrectableSectors.HasValue && info.UncorrectableSectors.Value > 0)
            {
                info.Status = "Critical";
                info.StatusReason = $"{info.UncorrectableSectors.Value} secteurs non corrigeables";
            }

            if (info.MediaErrors.HasValue && info.MediaErrors.Value > 0)
            {
                info.Status = "Warning";
                info.StatusReason = $"{info.MediaErrors.Value} erreurs média";
            }

            if (info.Temperature.HasValue && info.Temperature.Value > 55)
            {
                if (info.Status == "OK")
                {
                    info.Status = "Warning";
                    info.StatusReason = $"Température élevée : {info.Temperature.Value}°C";
                }
            }

            return info;
        }
    }
}