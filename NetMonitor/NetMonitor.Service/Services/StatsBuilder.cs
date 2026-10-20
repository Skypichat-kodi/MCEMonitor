using NetMonitor.Service.Models;

namespace NetMonitor.Service.Services;

public static class StatsBuilder
{
    public static async Task<GlobalStats> BuildAsync(
        NetMonitorEngine engine,
        GeoIpService geo,
        int topIpsLimit = 30,
        int topCountriesLimit = 15,
        int topDetectorsLimit = 15)
    {
        var alerts = engine.GetAlerts();
        var validEvents = alerts.Where(e => !string.IsNullOrWhiteSpace(e.RemoteIp)).ToList();

        var stats = new GlobalStats { TotalAlerts = validEvents.Count };

        foreach (var ev in validEvents)
        {
            string sev = ev.Severity.ToString();
            stats.SeverityCounts[sev] = stats.SeverityCounts.GetValueOrDefault(sev) + 1;
        }

        var byIp = validEvents
            .GroupBy(e => e.RemoteIp!)
            .Select(g =>
            {
                var mostSevere = g.OrderByDescending(e => (int)e.Severity).First();
                return new
                {
                    Ip = g.Key,
                    Count = g.Count(),
                    FirstSeen = g.Min(e => e.Timestamp),
                    LastSeen = g.Max(e => e.Timestamp),
                    MaxSeverity = mostSevere.Severity.ToString(),
                    Detectors = g.Select(e => e.Source).Distinct().OrderBy(x => x).ToList()
                };
            })
            .OrderByDescending(x => x.Count)
            .Take(topIpsLimit)
            .ToList();

        var ipStatsList = new List<IpStats>();
        foreach (var item in byIp)
        {
            var geoInfo = geo.TryGetCached(item.Ip) ?? await geo.ResolveAsync(item.Ip);
            ipStatsList.Add(new IpStats
            {
                Ip = item.Ip,
                Count = item.Count,
                FirstSeen = item.FirstSeen,
                LastSeen = item.LastSeen,
                MaxSeverity = item.MaxSeverity,
                Detectors = item.Detectors,
                Country = geoInfo.Country,
                CountryCode = geoInfo.CountryCode,
                City = geoInfo.City,
                Isp = geoInfo.Isp,
                ReverseDns = geoInfo.ReverseDns,
                IsPrivate = geoInfo.IsPrivate
            });
        }
        stats.TopIps = ipStatsList;

        var countries = ipStatsList
            .Where(x => !string.IsNullOrEmpty(x.Country) && !x.IsPrivate)
            .GroupBy(x => new { x.Country, x.CountryCode })
            .Select(g => new CountryStats
            {
                Country = g.Key.Country ?? "",
                CountryCode = g.Key.CountryCode ?? "",
                Count = g.Sum(x => x.Count),
                DistinctIps = g.Select(x => x.Ip).Distinct().Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(topCountriesLimit)
            .ToList();
        stats.TopCountries = countries;
        stats.DistinctCountries = countries.Count;

        stats.TopDetectors = validEvents
            .GroupBy(e => e.Source)
            .Select(g => new DetectorStats { Detector = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(topDetectorsLimit)
            .ToList();

        stats.DistinctIps = validEvents.Select(e => e.RemoteIp).Distinct().Count();
        stats.WorstSeverity = validEvents.Any(e => e.Severity == Severity.Critical) ? "critical"
                            : validEvents.Any(e => e.Severity == Severity.High) ? "warning"
                            : "ok";

        // ============================================================
        //  Carte 1 : intrusions actives
        // ============================================================
        var activeEvents = engine.GetActiveIntrusions()
            .Where(e => !string.IsNullOrWhiteSpace(e.RemoteIp))
            .ToList();

        var activeGrouped = activeEvents
            .GroupBy(e => e.RemoteIp!)
            .ToDictionary(g => g.Key, g => g.Count());

        var activeByCountry = new Dictionary<string, int>();
        foreach (var kv in activeGrouped)
        {
            var geoInfo = geo.TryGetCached(kv.Key);
            if (geoInfo == null)
                geoInfo = await geo.ResolveAsync(kv.Key);

            if (geoInfo != null && !geoInfo.IsPrivate &&
                !string.IsNullOrEmpty(geoInfo.CountryCode))
            {
                activeByCountry[geoInfo.CountryCode] =
                    activeByCountry.GetValueOrDefault(geoInfo.CountryCode) + kv.Value;
            }
        }
        stats.AlertsByCountryCodeActive = activeByCountry;

        // ============================================================
        //  Carte 2 : IP bloquées
        // ============================================================
        var blockedList = engine.GetBlockedIps()
            .Where(b => !string.IsNullOrWhiteSpace(b.Ip))
            .ToList();

        var blockedByCountry = new Dictionary<string, int>();
        int resolveBudget = 50;   // limite les lookups synchrones par appel

        foreach (var b in blockedList)
        {
            var geoInfo = geo.TryGetCached(b.Ip);

            if (geoInfo == null)
            {
                if (resolveBudget > 0)
                {
                    geoInfo = await geo.ResolveAsync(b.Ip);
                    resolveBudget--;
                }
                else
                {
                    geo.ResolveInBackground(b.Ip);   // résolution en arrière-plan
                    continue;
                }
            }

            if (geoInfo != null && !geoInfo.IsPrivate &&
                !string.IsNullOrEmpty(geoInfo.CountryCode))
            {
                blockedByCountry[geoInfo.CountryCode] =
                    blockedByCountry.GetValueOrDefault(geoInfo.CountryCode) + 1;
            }
        }
        stats.AlertsByCountryCodeBlocked = blockedByCountry;

        return stats;
    }
}