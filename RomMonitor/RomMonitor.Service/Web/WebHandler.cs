using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using MCEMonitor.Languages;

namespace RomMonitor.Service.Web
{
    public static class WebHandler
    {
        public static string BuildRomPage(RomMonitorEngine engine, RomMonitorSettings settings)
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

                    diskRows.Append($@"
                        <tr>
                            <td><b>{WebUtility.HtmlEncode(d.Name)}</b></td>
                            <td>{WebUtility.HtmlEncode(d.Label)}</td>
                            <td>{WebUtility.HtmlEncode(d.DriveType)}</td>
                            <td>{d.TotalGo:F1} Go</td>
                            <td>{d.FreeGo:F1} Go</td>
                            <td class='{freeClass}'>{d.FreePercent:F1} %</td>
                            <td><span class='badge {smartClass}'>{smartStatus}</span></td>
                            <td>{WebUtility.HtmlEncode(match?.Model ?? "")}</td>
                            <td>{tempText}</td>
                            <td>{hoursText}</td>
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

                return ViewRenderer.Render("RomPage.html", model);
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