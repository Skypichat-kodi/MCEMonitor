using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Globalization;
using MCEMonitor.Languages;
using RomMonitor.Service.Web;

namespace RomMonitor.Service.Web
{
    public static class WebHandler
    {
        public static string BuildRomPage(
            RomMonitorEngine engine,
            RomMonitorSettings settings,
            string publicHost = "localhost")
        {
            try
            {
                var disks = engine.LastDisks;
                var smart = engine.LastSmart;
                var alerts = engine.LastAlerts;

                // ============================================================
                //  1. Matching SMART ? disque physique (même logique que l'UI)
                // ============================================================
                var diskNumberToSmart = new Dictionary<int, SmartInfo>();
                var usedSmartSerials = new HashSet<string>();

                if (smart != null && smart.Count > 0)
                {
                    // Liste des disques physiques uniques
                    var uniqueDiskNumbers = new HashSet<int>();

                    foreach (var d in disks)
                    {
                        if (d.PhysicalDiskNumber.HasValue)
                            uniqueDiskNumbers.Add(d.PhysicalDiskNumber.Value);
                    }

                    // Pour chaque disque physique, on cherche son SMART
                    foreach (int diskNum in uniqueDiskNumbers)
                    {
                        // Taille du disque physique
                        double physicalSize = 0;

                        foreach (var d in disks)
                        {
                            if (d.PhysicalDiskNumber == diskNum && d.PhysicalSizeGo > 0)
                            {
                                physicalSize = d.PhysicalSizeGo;
                                break;
                            }
                        }

                        if (physicalSize <= 0)
                            continue;

                        SmartInfo? best = null;
                        double bestDelta = double.MaxValue;

                        foreach (var s in smart)
                        {
                            if (s.CapacityGo <= 0) continue;
                            if (usedSmartSerials.Contains(s.Serial ?? "")) continue;

                            double delta = Math.Abs(s.CapacityGo - physicalSize) / physicalSize;

                            if (delta < 0.05 && delta < bestDelta)
                            {
                                best = s;
                                bestDelta = delta;
                            }
                        }

                        if (best != null)
                        {
                            diskNumberToSmart[diskNum] = best;
                            if (!string.IsNullOrEmpty(best.Serial))
                                usedSmartSerials.Add(best.Serial);
                        }
                    }
                }

                // ============================================================
                //  2. Construction des lignes du tableau disques
                // ============================================================
                var diskRows = new System.Text.StringBuilder();

                foreach (var d in disks)
                {
                    // Match UNIQUEMENT par numéro de disque physique
                    SmartInfo? match = null;

                    if (d.PhysicalDiskNumber.HasValue &&
                        diskNumberToSmart.TryGetValue(d.PhysicalDiskNumber.Value, out var m))
                    {
                        match = m;
                    }

                    string freeClass = d.FreePercent < 5 ? "danger"
                                     : d.FreePercent < 15 ? "warning"
                                     : "ok";

                    // Formate avec point décimal (culture invariante) pour que ce soit CSS-compatible
                    string usedPercent = (100 - d.FreePercent).ToString("F1", CultureInfo.InvariantCulture);
                    string freePercent = d.FreePercent.ToString("F1", CultureInfo.InvariantCulture);
                    string totalGo     = d.TotalGo.ToString("F1", CultureInfo.InvariantCulture);
                    string freeGo      = d.FreeGo.ToString("F1", CultureInfo.InvariantCulture);

                    string barColor = d.FreePercent < 5  ? "#FF6347"   // rouge
                                    : d.FreePercent < 15 ? "#FFB900"   // jaune
                                    : "#6CCB5F";                       // vert
                
                    string smartStatus = match?.Status ?? (LanguageManager.Get("N/A") ?? "N/A");
                    string smartClass = smartStatus == "OK" ? "ok"
                                      : smartStatus == "Warning" ? "warning"
                                      : smartStatus == "Critical" ? "danger"
                                      : "na";

                    string tempText = match?.Temperature.HasValue == true
                        ? $"{match.Temperature}°C"
                        : (LanguageManager.Get("N/A") ?? "N/A");

                    // ? NOUVEAU : heures de fonctionnement
                    string hoursText = match?.PowerOnHours.HasValue == true
                        ? $"{match.PowerOnHours.Value:N0} h"
                        : (LanguageManager.Get("N/A") ?? "N/A");

                    string hoursRaw = match?.PowerOnHours?.ToString(CultureInfo.InvariantCulture) ?? "";
                    string tempRaw  = match?.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "";

                    diskRows.Append($@"
                        <tr>
                            <td data-sort=""{WebUtility.HtmlEncode(d.Name)}""><b>{WebUtility.HtmlEncode(d.Name)}</b></td>
                            <td data-sort=""{WebUtility.HtmlEncode(d.Label)}"">{WebUtility.HtmlEncode(d.Label)}</td>
                            <td data-sort=""{WebUtility.HtmlEncode(d.DriveType)}"">{WebUtility.HtmlEncode(d.DriveType)}</td>
                            <td data-sort=""{d.TotalGo.ToString("F2", CultureInfo.InvariantCulture)}"">{totalGo} Go</td>
                            <td data-sort=""{d.FreeGo.ToString("F2", CultureInfo.InvariantCulture)}"">{freeGo} Go</td>
                            <td data-sort=""{d.FreePercent.ToString("F2", CultureInfo.InvariantCulture)}"">
                                <div class=""progress-cell"">
                                    <div class=""progress-bar"">
                                        <div class=""progress-fill"" style=""width:{usedPercent}%;background:{barColor}""></div>
                                    </div>
                                    <span class=""progress-text {freeClass}"">{freePercent} %</span>
                                </div>
                            </td>
                            <td data-sort=""{smartClass}""><span class='badge {smartClass}'>{smartStatus}</span></td>
                            <td data-sort=""{WebUtility.HtmlEncode(match?.Model ?? "")}"">{WebUtility.HtmlEncode(match?.Model ?? "")}</td>
                            <td data-sort=""{tempRaw}"">{tempText}</td>
                            <td data-sort=""{hoursRaw}"">{hoursText}</td>
                        </tr>");
                }

                // ============================================================
                //  3. Construction des lignes des alertes
                // ============================================================
                var alertRows = new System.Text.StringBuilder();

                foreach (var a in alerts.OrderByDescending(x => x.Timestamp).Take(50))
                {
                    string sevClass = a.Severity == "Critical" ? "danger"
                                    : a.Severity == "Warning" ? "warning"
                                    : "ok";

                    alertRows.Append($@"
                        <tr>
                            <td>{a.Timestamp:dd/MM HH:mm:ss}</td>
                            <td><span class='badge {sevClass}'>{a.Severity}</span></td>
                            <td>{WebUtility.HtmlEncode(a.Target)}</td>
                            <td>{WebUtility.HtmlEncode(a.Message)}</td>
                        </tr>");
                }

                // ============================================================
                //  4. Modèle final
                // ============================================================
                var model = new Dictionary<string, object?>
                {
                    ["MachineName"] = Environment.MachineName,
                    ["LastCheck"] = engine.LastCheckTime == DateTime.MinValue
                        ? (LanguageManager.Get("Jamais") ?? "Jamais")
                        : engine.LastCheckTime.ToString("dd/MM/yyyy HH:mm:ss"),
                    ["DiskCount"] = disks.Count,
                    ["SmartCount"] = smart.Count,
                    ["AlertCount"] = alerts.Count,
                    ["DiskRows"] = diskRows.ToString(),
                    ["AlertRows"] = alertRows.ToString()
                };

                string html = ViewRenderer.Render("RomPage.html", model);
                html = html.Replace("<!--PEERBAR-->", PeerStatusService.BuildPeerBarHtml(publicHost));
                return html;
            }
            catch (Exception ex)
            {
                CoreLog.Write("WebHandler ERROR : " + ex.Message);
                string err = LanguageManager.Get("Erreur") ?? "Erreur";
                return $"<html><body><h2>{err} : {WebUtility.HtmlEncode(ex.Message)}</h2></body></html>";
            }
        }
    }
}