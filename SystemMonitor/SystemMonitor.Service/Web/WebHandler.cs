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
                //  Cœurs CPU (barres verticales)
                // ============================================================
                var cpuCoreBars = new System.Text.StringBuilder();

                foreach (var core in snap.Cpu.Cores)
                {
                    string coreColor = GetColorForPercent(core.UsagePercent);
                    string coreUsage = core.UsagePercent.ToString("F0", CultureInfo.InvariantCulture);
                    string coreName = WebUtility.HtmlEncode(core.Name);

                    cpuCoreBars.Append($@"
                        <div class='core-item' title='{coreName} : {coreUsage}%'>
                            <div class='core-label'>{coreName}</div>
                            <div class='core-progress'>
                                <div class='core-bar' style='height:{coreUsage}%;background:{coreColor}'></div>
                            </div>
                            <div class='core-value'>{coreUsage}%</div>
                        </div>");
                }

                // ============================================================
                //  Graphiques historiques (5 min)
                // ============================================================
                var history = engine.GetHistory();

                var cpuValues = history.Select(h => h.CpuUsage).ToList();
                var ramValues = history.Select(h => h.RamUsage).ToList();

                string cpuChartSvg = GenerateSvgChart(cpuValues, "#4CC2FF");
                string ramChartSvg = GenerateSvgChart(ramValues, "#6CCB5F");

                string cpuMaxText = cpuValues.Count > 0
                    ? cpuValues.Max().ToString("F1", CultureInfo.InvariantCulture)
                    : "0";

                string ramMaxText = ramValues.Count > 0
                    ? ramValues.Max().ToString("F1", CultureInfo.InvariantCulture)
                    : "0";

                string historyCountText = history.Count.ToString();

                // ============================================================
                //  Alertes récentes
                // ============================================================
                var alerts = engine.GetAlerts();
                var alertRows = new System.Text.StringBuilder();

                if (alerts != null && alerts.Count > 0)
                {
                    foreach (var a in alerts.OrderByDescending(x => x.Timestamp).Take(50))
                    {
                        string sevClass = a.Severity == "Critical" ? "danger"
                                        : a.Severity == "Warning" ? "warning"
                                        : "ok";

                        alertRows.Append($@"
                            <tr>
                                <td>{a.Timestamp:dd/MM HH:mm:ss}</td>
                                <td><span class='badge {sevClass}'>{WebUtility.HtmlEncode(a.Severity)}</span></td>
                                <td>{WebUtility.HtmlEncode(a.Type.ToString())}</td>
                                <td>{WebUtility.HtmlEncode(a.Target)}</td>
                                <td>{WebUtility.HtmlEncode(a.Message)}</td>
                            </tr>");
                    }
                }
                else
                {
                    alertRows.Append("<tr><td colspan='5' style='text-align:center;color:#888;padding:20px'>Aucune alerte</td></tr>");
                }

                // ============================================================
                //  BSOD
                // ============================================================
                var bsods = engine.GetBsods();
                var bsodRows = new System.Text.StringBuilder();

                if (bsods != null && bsods.Count > 0)
                {
                    foreach (var b in bsods.OrderByDescending(x => x.Timestamp).Take(50))
                    {
                        string dumpIcon = b.DumpExists ? "?" : "?";

                        bsodRows.Append($@"
                            <tr>
                                <td>{b.Timestamp:dd/MM/yyyy HH:mm:ss}</td>
                                <td><b>{WebUtility.HtmlEncode(b.BugCheckCode)}</b></td>
                                <td>{WebUtility.HtmlEncode(b.BugCheckName)}</td>
                                <td>{WebUtility.HtmlEncode(b.FaultyModule)}</td>
                                <td style='text-align:center'>{dumpIcon}</td>
                            </tr>");
                    }
                }
                else
                {
                    bsodRows.Append("<tr><td colspan='5' style='text-align:center;color:#888;padding:20px'>Aucun BSOD</td></tr>");
                }

                int alertCount = alerts?.Count ?? 0;
                int bsodCount = bsods?.Count ?? 0;
                                                                
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
                    ["HasCores"] = snap.Cpu.Cores.Count > 0,                
                    ["CoreCount"] = snap.Cpu.Cores.Count,
                    ["CoreBars"] = cpuCoreBars.ToString(),

                    // RAM
                    ["RamTotal"] = snap.Ram.TotalGB.ToString("F1", CultureInfo.InvariantCulture),
                    ["RamUsed"] = snap.Ram.UsedGB.ToString("F1", CultureInfo.InvariantCulture),
                    ["RamFree"] = snap.Ram.FreeGB.ToString("F1", CultureInfo.InvariantCulture),
                    ["RamUsage"] = snap.Ram.UsagePercent.ToString("F1", CultureInfo.InvariantCulture),
                    ["RamBarColor"] = GetColorForPercent(snap.Ram.UsagePercent),
                    ["RamUsageClass"] = ramClass,

                    //Graphiques historique
                    ["CpuChartSvg"] = cpuChartSvg,
                    ["RamChartSvg"] = ramChartSvg,
                    ["CpuMaxText"] = cpuMaxText,
                    ["RamMaxText"] = ramMaxText,
                    ["HistoryCount"] = historyCountText,

                    // Infos BSOD
                    ["AlertRows"] = alertRows.ToString(),
                    ["AlertTotalCount"] = alertCount,
                    ["BsodRows"] = bsodRows.ToString(),
                    ["BsodTotalCount"] = bsodCount,
                                        
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
        
        /// <summary>
        /// Génère un graphique SVG à partir d'une série de valeurs (0-100).
        /// </summary>
        private static string GenerateSvgChart(
            List<double> values,
            string color,
            int width = 800,
            int height = 180,
            double maxValue = 100)
        {
            if (values == null || values.Count < 2)
                return "<div style='color:#888;padding:20px;text-align:center'>Pas assez de données</div>";

            var sb = new System.Text.StringBuilder();
            var inv = CultureInfo.InvariantCulture;   // ? Important : force le point comme séparateur

            int padding = 10;
            int drawWidth = width - padding * 2;
            int drawHeight = height - padding * 2;
            int bottomY = padding + drawHeight;

            // 1. Calculer tous les points (x, y) une seule fois
            var points = new List<(double x, double y)>();
            double stepX = drawWidth / (double)(values.Count - 1);

            for (int i = 0; i < values.Count; i++)
            {
                double v = Math.Max(0, Math.Min(maxValue, values[i]));
                double x = padding + i * stepX;
                double y = padding + drawHeight - (v / maxValue * drawHeight);
                points.Add((x, y));
            }

            // 2. Générer le SVG
            sb.Append($"<svg viewBox='0 0 {width} {height}' preserveAspectRatio='none' ");
            sb.Append($"style='width:100%;height:{height}px;background:#1A1A1A;border-radius:6px'>");

            // Grille horizontale
            for (int i = 1; i <= 3; i++)
            {
                double y = padding + drawHeight * (i / 4.0);
                sb.Append($"<line x1='{padding}' y1='{y.ToString("F1", inv)}' x2='{width - padding}' y2='{y.ToString("F1", inv)}' ");
                sb.Append($"stroke='#2D2D30' stroke-width='1'/>");
            }

            // 3. Zone remplie sous la courbe (path fermé)
            sb.Append("<path d='");
            sb.Append($"M {padding},{bottomY} ");
            sb.Append($"L {points[0].x.ToString("F1", inv)},{points[0].y.ToString("F1", inv)} ");

            for (int i = 1; i < points.Count; i++)
                sb.Append($"L {points[i].x.ToString("F1", inv)},{points[i].y.ToString("F1", inv)} ");

            sb.Append($"L {points[points.Count - 1].x.ToString("F1", inv)},{bottomY} Z' ");
            sb.Append($"fill='{color}' fill-opacity='0.15' stroke='none'/>");

            // 4. Courbe (polyline)
            sb.Append("<polyline points='");
            for (int i = 0; i < points.Count; i++)
            {
                sb.Append($"{points[i].x.ToString("F1", inv)},{points[i].y.ToString("F1", inv)}");
                if (i < points.Count - 1)
                    sb.Append(' ');
            }
            sb.Append($"' fill='none' stroke='{color}' stroke-width='2' ");
            sb.Append("stroke-linejoin='round' stroke-linecap='round'/>");

            sb.Append("</svg>");

            return sb.ToString();
        }
    }
}