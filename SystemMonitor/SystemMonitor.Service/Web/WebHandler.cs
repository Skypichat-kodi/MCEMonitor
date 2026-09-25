using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using MCEMonitor.Languages;

namespace SystemMonitor.Service.Web
{
    public static class WebHandler
    {
        public static string BuildSystemPage(SystemMonitorEngine engine, SystemMonitorSettings settings)
        {
            try
            {
                var snap = engine.LastSnapshot;

                // ============================================================
                //  GPU (boucle)
                // ============================================================
                var gpuRows = new System.Text.StringBuilder();

                foreach (var g in snap.Gpus)
                {
                    string tempClass = (g.Temperature ?? 0) >= 85 ? "danger"
                                     : (g.Temperature ?? 0) >= 70 ? "warning"
                                     : "ok";

                    string gpuUsageStr = g.UsagePercent.ToString("F1", CultureInfo.InvariantCulture);
                    string gpuTempStr = g.Temperature.HasValue
                        ? g.Temperature.Value.ToString("F1", CultureInfo.InvariantCulture) + " °C"
                        : "N/A";
                    string gpuVramStr = (g.VramUsedMB.HasValue && g.VramTotalMB.HasValue)
                        ? g.VramUsedMB.Value.ToString("F0", CultureInfo.InvariantCulture) + " / " +
                          g.VramTotalMB.Value.ToString("F0", CultureInfo.InvariantCulture) + " Mo"
                        : "N/A";

                    gpuRows.Append($@"
                        <tr>
                            <td>{WebUtility.HtmlEncode(g.Name)}</td>
                            <td>{gpuUsageStr} %</td>
                            <td class='{tempClass}'>{gpuTempStr}</td>
                            <td>{gpuVramStr}</td>
                        </tr>");
                }

                // ============================================================
                //  Réseau (boucle)
                // ============================================================
                var netRows = new System.Text.StringBuilder();

                foreach (var n in snap.Networks)
                {
                    string downStr = n.DownloadKBps.ToString("F1", CultureInfo.InvariantCulture) + " KB/s";
                    string upStr = n.UploadKBps.ToString("F1", CultureInfo.InvariantCulture) + " KB/s";

                    netRows.Append($@"
                        <tr>
                            <td>{WebUtility.HtmlEncode(n.Name)}</td>
                            <td>{downStr}</td>
                            <td>{upStr}</td>
                        </tr>");
                }

                // ============================================================
                //  Classes de couleur (pour le texte)
                // ============================================================
                string cpuUsageClass = snap.Cpu.UsagePercent >= 90 ? "danger"
                                     : snap.Cpu.UsagePercent >= 70 ? "warning"
                                     : "ok";

                string cpuTempClass = (snap.Cpu.Temperature ?? 0) >= 85 ? "danger"
                                    : (snap.Cpu.Temperature ?? 0) >= 70 ? "warning"
                                    : "ok";

                string ramClass = snap.Ram.UsagePercent >= 90 ? "danger"
                                : snap.Ram.UsagePercent >= 70 ? "warning"
                                : "ok";

                // ============================================================
                //  Cœurs CPU
                // ============================================================
                var coreBars = new System.Text.StringBuilder();

                foreach (var core in snap.Cpu.Cores)
                {
                    string color = GetColorForPercent(core.UsagePercent);
                    string usage = core.UsagePercent.ToString("F0", CultureInfo.InvariantCulture);

                    coreBars.Append($@"
                        <div class='core-item' title='{WebUtility.HtmlEncode(core.Name)} : {usage}%'>
                            <div class='core-label'>{WebUtility.HtmlEncode(core.Name)}</div>
                            <div class='core-progress'>
                                <div class='core-bar' style='height:{usage}%;background:{color}'></div>
                            </div>
                            <div class='core-value'>{usage}%</div>
                        </div>");
                }
                
                // ============================================================
                //  Modèle
                // ============================================================
                var model = new Dictionary<string, object?>
                {
                    ["MachineName"] = Environment.MachineName,
                    ["LastUpdate"] = engine.LastUpdateTime == DateTime.MinValue
                        ? (LanguageManager.Get("Jamais") ?? "Jamais")
                        : engine.LastUpdateTime.ToString("dd/MM/yyyy HH:mm:ss"),

                    // CPU
                    ["CpuName"] = WebUtility.HtmlEncode(snap.Cpu.Name),
                    ["CpuUsage"] = snap.Cpu.UsagePercent.ToString("F1", CultureInfo.InvariantCulture),
                    ["CpuBarColor"] = GetColorForPercent(snap.Cpu.UsagePercent),
                    ["CpuUsageClass"] = cpuUsageClass,
                    ["CpuTemp"] = snap.Cpu.Temperature?.ToString("F1", CultureInfo.InvariantCulture) ?? "N/A",
                    ["CpuTempClass"] = cpuTempClass,
                    ["CpuFreq"] = snap.Cpu.FrequencyMHz?.ToString("F0", CultureInfo.InvariantCulture) ?? "N/A",
                    ["CpuMaxFreq"] = snap.Cpu.MaxFrequencyMHz?.ToString("F0", CultureInfo.InvariantCulture) ?? "N/A",
                    ["CoreCount"] = snap.Cpu.Cores.Count,
                    ["CoreBars"] = coreBars.ToString(),

                    // RAM
                    ["RamTotal"] = snap.Ram.TotalGB.ToString("F1", CultureInfo.InvariantCulture),
                    ["RamUsed"] = snap.Ram.UsedGB.ToString("F1", CultureInfo.InvariantCulture),
                    ["RamFree"] = snap.Ram.FreeGB.ToString("F1", CultureInfo.InvariantCulture),
                    ["RamUsage"] = snap.Ram.UsagePercent.ToString("F1", CultureInfo.InvariantCulture),
                    ["RamBarColor"] = GetColorForPercent(snap.Ram.UsagePercent),
                    ["RamUsageClass"] = ramClass,

                    // Boucles
                    ["GpuRows"] = gpuRows.ToString(),
                    ["NetRows"] = netRows.ToString(),

                    // Divers
                    ["GpuCount"] = snap.Gpus.Count,
                    ["NetCount"] = snap.Networks.Count
                };

                return ViewRenderer.Render("SystemPage.html", model);
            }
            catch (Exception ex)
            {
                CoreLog.Write("WebHandler ERROR : " + ex.Message);
                string err = LanguageManager.Get("Erreur") ?? "Erreur";
                return $"<html><body><h2>{err} : {WebUtility.HtmlEncode(ex.Message)}</h2></body></html>";
            }
        }

        /// <summary>
        /// Retourne une couleur hexadécimale selon un pourcentage.
        /// Vert (0-60%), dégradé vers orange (60-85%), puis rouge (85-100%).
        /// </summary>
        private static string GetColorForPercent(double percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            byte r, g, b;

            if (percent <= 60)
            {
                // Vert fixe
                r = 108; g = 203; b = 95;
            }
            else if (percent <= 85)
            {
                // Interpolation vert ? orange
                double t = (percent - 60) / 25.0;
                r = (byte)(108 + (255 - 108) * t);
                g = (byte)(203 + (185 - 203) * t);
                b = (byte)(95  + (0   - 95)  * t);
            }
            else
            {
                // Interpolation orange ? rouge
                double t = (percent - 85) / 15.0;
                r = (byte)(255 + (255 - 255) * t);
                g = (byte)(185 + (99  - 185) * t);
                b = (byte)(0   + (71  - 0)   * t);
            }

            return $"#{r:X2}{g:X2}{b:X2}";
        }
    }
}