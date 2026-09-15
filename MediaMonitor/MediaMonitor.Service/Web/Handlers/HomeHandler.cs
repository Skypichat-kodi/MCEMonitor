using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using MediaMonitor.Core.Services;
using MediaMonitor.Service.Web.Models;

namespace MediaMonitor.Service.Web.Handlers
{
    public class HomeHandler
    {
        private readonly MediaMonitorEngine _engine;
        private readonly int _port;

        // Ces compteurs restent sur le WebServer, on les passe en paramètre.
        public HomeHandler(MediaMonitorEngine engine, int port)
        {
            _engine = engine;
            _port = port;
        }

        public string Render(long requestCount, DateTime lastRequestTime, string lastRequestIp)
        {
            var live = _engine.GetCurrentOpenFiles();
            var history = _engine.GetHistory();

            int liveCount = live.Count;
            int historyCount = history.Count;
            int history24h = history.Count(h => h.Timestamp >= DateTime.Now.AddHours(-24));

            var lastReport = GetLastReportTime();
            var nextReport = GetReportSendTime();

            var model = new Dictionary<string, object?>
            {
                // Bloc service
                ["MachineName"]      = WebUtility.HtmlEncode(Environment.MachineName),
                ["CurrentTime"]      = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),

                // Bloc rapports
                ["IfHasReport"] = lastReport != DateTime.MinValue,
                ["IfNoReport"]  = lastReport == DateTime.MinValue,
                ["LastReport"]  = lastReport == DateTime.MinValue
                                  ? ""
                                  : lastReport.ToString("yyyy-MM-dd HH:mm:ss"),

                ["IfHasNextReport"] = nextReport != DateTime.MinValue,
                ["IfNoNextReport"]  = nextReport == DateTime.MinValue,
                ["NextReport"]      = nextReport == DateTime.MinValue
                      ? ""
                      : nextReport.ToString("yyyy-MM-dd HH:mm:ss"),

                // Bloc lecture
                ["LiveCount"]        = liveCount,
                ["ActiveUsers"]      = live.Select(x => x.ClientDisplay)
                                           .Where(n => !string.IsNullOrWhiteSpace(n))
                                           .Distinct().Count(),

                // Bloc historique
                ["HistoryCount"]     = historyCount,
                ["History24h"]       = history24h,
                ["HasHistory"]       = historyCount > 0,
                ["LastEvent"]        = historyCount > 0
                                        ? $"{history.Last().Timestamp:HH:mm:ss} – " +
                                          $"{WebUtility.HtmlEncode(history.Last().MediaType)} " +
                                          $"({WebUtility.HtmlEncode(history.Last().ClientDisplay)})"
                                        : "",

                // Bloc webserver
                ["Port"]             = _port,
                ["RequestCount"]     = requestCount,
                ["LastRequestTime"]  = lastRequestTime == DateTime.MinValue
                                        ? "N/A"
                                        : lastRequestTime.ToString("HH:mm:ss"),
                ["LastRequestIp"]    = WebUtility.HtmlEncode(lastRequestIp),

                // Tableau lecture
                ["LiveItems"]        = live.Select(i => ViewHelpers.ToViewDict(
                                            i.ClientDisplay ?? "", i.MediaType ?? "",
                                            i.Channel ?? "", i.Nom ?? "",
                                            i.FileName ?? "", i.Path ?? "",
                                            i.Saison, i.Episode)).ToList(),
                ["NoLive"]           = liveCount == 0,

                // Tableau historique (200 derniers, ordre décroissant)
                ["HistoryItems"]     = history
                                        .OrderByDescending(h => h.Timestamp)
                                        .Take(200)
                                        .Select(i =>
                                        {
                                            var d = ViewHelpers.ToViewDict(
                                                i.ClientDisplay ?? "", i.MediaType ?? "",
                                                i.Channel ?? "", i.Nom ?? "",
                                                i.FileName ?? "", i.Path ?? "",
                                                i.Saison, i.Episode);
                                            d["Time"] = i.Timestamp.ToString("HH:mm:ss");
                                            return d;
                                        }).ToList(),
                ["NoHistory"]        = historyCount == 0
            };

            return ViewRenderer.Render("Home.html", model);
        }

        private DateTime GetLastReportTime()
        {
            string path = @"C:\ProgramData\MCEMonitor\Logs\MediaMonitor.Schedule.log";
            if (!File.Exists(path)) return DateTime.MinValue;

            // Nouveau format : "[CODE02]|2026-09-15 20:10:08"
            string lastLine = File.ReadLines(path)
                .LastOrDefault(l => l.Contains("[CODE02]|"));
            if (lastLine == null) return DateTime.MinValue;

            int idx = lastLine.IndexOf("[CODE02]|");
            if (idx < 0) return DateTime.MinValue;

            string value = lastLine.Substring(idx + "[CODE02]|".Length).Trim();

            // "AUCUN" ? pas de rapport
            if (value.Equals("AUCUN", StringComparison.OrdinalIgnoreCase))
                return DateTime.MinValue;

            return DateTime.TryParse(value, out var dt) ? dt : DateTime.MinValue;
        }

        private DateTime GetReportSendTime()
        {
            string path = @"C:\ProgramData\MCEMonitor\Logs\MediaMonitor.Schedule.log";
            if (!File.Exists(path)) return DateTime.MinValue;

            // Nouveau format : "[CODE01]|11:50|15h 39min"
            string lastLine = File.ReadLines(path)
                .LastOrDefault(l => l.Contains("[CODE01]|"));
            if (lastLine == null) return DateTime.MinValue;

            int idx = lastLine.IndexOf("[CODE01]|");
            if (idx < 0) return DateTime.MinValue;

            string value = lastLine.Substring(idx + "[CODE01]|".Length).Trim();

            // value = "11:50|15h 39min"
            var parts = value.Split('|');
            if (parts.Length == 0) return DateTime.MinValue;

            string timePart = parts[0]; // "11:50"

            if (TimeSpan.TryParse(timePart, out var ts))
            {
                DateTime next = DateTime.Today.Add(ts);
                if (next <= DateTime.Now) next = next.AddDays(1);
                return next;
            }
            return DateTime.MinValue;
        }
    }
}