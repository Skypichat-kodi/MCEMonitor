using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace SystemMonitor.Service
{
    /// <summary>
    /// Capture les processus les plus consommateurs (CPU + RAM)
    /// au moment d'une alerte. Échantillonnage sur ~1 seconde.
    /// </summary>
    public static class TopProcessesCollector
    {
        public class ProcessUsage
        {
            public string Name    { get; set; } = "";
            public int    Pid     { get; set; }
            public double CpuPct  { get; set; }
            public double RamMB   { get; set; }
        }

        public class Report
        {
            public List<ProcessUsage> TopCpu { get; set; } = new();
            public List<ProcessUsage> TopRam { get; set; } = new();
        }

        /// <summary>
        /// Échantillonne tous les processus sur sampleMs puis renvoie
        /// les Top N par CPU et par RAM.
        /// </summary>
        public static Report Collect(int topN = 5, int sampleMs = 1000)
        {
            var report = new Report();

            try
            {
                int coreCount = Environment.ProcessorCount;
                var snapshot1 = new Dictionary<int, TimeSpan>();
                var procs     = new Dictionary<int, Process>();

                // -------- Snapshot 1 --------
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        snapshot1[p.Id] = p.TotalProcessorTime;
                        procs[p.Id]     = p;
                    }
                    catch { /* accès refusé sur certains process système */ }
                }

                Thread.Sleep(sampleMs);

                // -------- Snapshot 2 + calcul --------
                var usages = new List<ProcessUsage>();

                foreach (var kv in procs)
                {
                    try
                    {
                        var p = kv.Value;
                        p.Refresh();

                        // TotalProcessorTime peut lever si le process est terminé
                        var delta = p.TotalProcessorTime - snapshot1[p.Id];

                        double cpuPct = delta.TotalMilliseconds
                                        / sampleMs
                                        / coreCount
                                        * 100.0;

                        // Borne de sécurité (peut légèrement dépasser à cause du timing)
                        if (cpuPct < 0)   cpuPct = 0;
                        if (cpuPct > 100) cpuPct = 100;

                        usages.Add(new ProcessUsage
                        {
                            Name   = p.ProcessName,
                            Pid    = p.Id,
                            CpuPct = cpuPct,
                            RamMB  = p.WorkingSet64 / 1024.0 / 1024.0
                        });
                    }
                    catch { /* process disparu pendant l'échantillonnage */ }
                }

                report.TopCpu = usages
                    .OrderByDescending(u => u.CpuPct)
                    .Take(topN)
                    .ToList();

                report.TopRam = usages
                    .OrderByDescending(u => u.RamMB)
                    .Take(topN)
                    .ToList();
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur TopProcessesCollector : " + ex.Message);
            }

            return report;
        }

        /// <summary>
        /// Construit un bloc HTML prêt à insérer dans le mail d'alerte.
        /// </summary>
        public static string BuildHtmlBlock(Report? report, bool forCpu)
        {
            if (report == null)
                return "";

            var list = forCpu ? report.TopCpu : report.TopRam;
            if (list.Count == 0)
                return "";

            string title = forCpu
                ? "Top processus — Consommation CPU"
                : "Top processus — Consommation RAM";

            var sb = new StringBuilder();

            sb.Append($@"
            <h3 style='margin:20px 0 8px 0;color:#4CC2FF;font-size:14px'>{title}</h3>
            <table style='width:100%;border-collapse:collapse;font-size:12px;
                          border:1px solid #3c3c3c;border-radius:6px;overflow:hidden'>
                <thead>
                    <tr style='background:#2d2d30'>
                        <th style='padding:6px 8px;text-align:left;color:#fff'>Processus</th>
                        <th style='padding:6px 8px;text-align:center;color:#fff;width:70px'>PID</th>
                        <th style='padding:6px 8px;text-align:right;color:#fff;width:90px'>CPU</th>
                        <th style='padding:6px 8px;text-align:right;color:#fff;width:100px'>RAM</th>
                    </tr>
                </thead>
                <tbody>");

            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                string bg = (i % 2 == 0) ? "#1f1f1f" : "#262626";

                // Colore la valeur CPU en rouge si > 50 %
                string cpuColor = forCpu
                    ? (u.CpuPct > 50 ? "#FF6347" : (u.CpuPct > 20 ? "#FFB900" : "#6CCB5F"))
                    : "#E5E5E5";

                sb.Append($@"
                    <tr style='background:{bg}'>
                        <td style='padding:6px 8px;color:#E5E5E5'>{u.Name}</td>
                        <td style='padding:6px 8px;color:#888;text-align:center'>{u.Pid}</td>
                        <td style='padding:6px 8px;color:{cpuColor};text-align:right;font-weight:bold'>
                            {u.CpuPct:F1} %
                        </td>
                        <td style='padding:6px 8px;color:#E5E5E5;text-align:right'>
                            {u.RamMB:F0} Mo
                        </td>
                    </tr>");
            }

            sb.Append("</tbody></table>");
            return sb.ToString();
        }
    }
}