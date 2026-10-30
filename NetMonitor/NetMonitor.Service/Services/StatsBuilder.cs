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
        var stats = new GlobalStats { GeneratedAt = DateTime.UtcNow };

        // ========= Global summary (toutes les alertes) =========
        var alerts = engine.GetAlerts();
        var validAlerts = alerts.Where(e => !string.IsNullOrWhiteSpace(e.RemoteIp)).ToList();
        stats.TotalAlerts = validAlerts.Count;
        stats.DistinctIps = validAlerts.Select(e => e.RemoteIp).Distinct().Count();

        foreach (var ev in validAlerts)
        {
            string sev = ev.Severity.ToString();
            stats.SeverityCounts[sev] = stats.SeverityCounts.GetValueOrDefault(sev) + 1;
        }

        stats.WorstSeverity = validAlerts.Any(e => e.Severity == Severity.Critical) ? "critical"
                            : validAlerts.Any(e => e.Severity == Severity.High ||
                                                   e.Severity == Severity.Warning) ? "warning"
                            : "ok";

        var globalCountries = new HashSet<string>();
        foreach (var ev in validAlerts)
        {
            var gi = geo.TryGetCached(ev.RemoteIp!);
            if (gi == null) { geo.ResolveInBackground(ev.RemoteIp!); continue; }
            if (!gi.IsPrivate && !string.IsNullOrEmpty(gi.CountryCode))
                globalCountries.Add(gi.CountryCode);
        }
        stats.DistinctCountries = globalCountries.Count;

        // ========= Bloc 1 : intrusions actives =========
        var activeEvents = engine.GetActiveIntrusions()
            .Where(e => !string.IsNullOrWhiteSpace(e.RemoteIp))
            .ToList();
        stats.Active = await BuildBlockFromEventsAsync(activeEvents, geo,
            topIpsLimit, topCountriesLimit, topDetectorsLimit);

        // ========= Bloc 2 : IP bloquées =========
        // On EXCLUT les processus bloqués : ils n'ont pas d'IP résolvable,
        // fausseraient DistinctIps / DistinctCountries et le GeoIP.
        var blockedList = engine.GetBlockedIps()
            .Where(b => !string.IsNullOrWhiteSpace(b.Ip) && !b.IsProcess)
            .ToList();
        stats.Blocked = await BuildBlockFromBlockedAsync(blockedList, geo,
            topIpsLimit, topCountriesLimit, topDetectorsLimit);

        // ============================================================
        //  Analyses enrichies (ports, heures, jours, processus…)
        // ============================================================

        // --- Top 10 ports locaux ciblés ---
        stats.TopLocalPorts = validAlerts
            .Where(e => e.LocalPort > 0)
            .GroupBy(e => e.LocalPort)
            .Select(g => new PortStats
            {
                Port = g.Key,
                Count = g.Count(),
                TopDetectors = g.Select(e => e.Source).Distinct().Take(3).ToList()
            })
            .OrderByDescending(p => p.Count)
            .Take(10)
            .ToList();

        // --- Top 10 ports distants (source d'attaque) ---
        stats.TopRemotePorts = validAlerts
            .Where(e => e.RemotePort > 0)
            .GroupBy(e => e.RemotePort)
            .Select(g => new PortStats
            {
                Port = g.Key,
                Count = g.Count(),
                TopDetectors = g.Select(e => e.Source).Distinct().Take(3).ToList()
            })
            .OrderByDescending(p => p.Count)
            .Take(10)
            .ToList();

        // --- Activité par heure (24 buckets, y compris heures vides) ---
        var hourMap = validAlerts
            .GroupBy(e => e.Timestamp.Hour)
            .ToDictionary(g => g.Key, g => new
            {
                Count = g.Count(),
                Critical = g.Count(e => e.Severity == Severity.Critical)
            });

        stats.ActivityByHour = Enumerable.Range(0, 24)
            .Select(h => new HourlyStats
            {
                Hour = h,
                Count = hourMap.TryGetValue(h, out var v) ? v.Count : 0,
                CriticalCount = hourMap.TryGetValue(h, out var cv) ? cv.Critical : 0
            })
            .ToList();

        // --- Activité par jour de la semaine (7 buckets) ---
        string[] dayNames = { "Dimanche", "Lundi", "Mardi", "Mercredi", "Jeudi", "Vendredi", "Samedi" };
        var dayMap = validAlerts
            .GroupBy(e => (int)e.Timestamp.DayOfWeek)
            .ToDictionary(g => g.Key, g => g.Count());

        stats.ActivityByDay = Enumerable.Range(0, 7)
            .Select(d => new DailyStats
            {
                DayOfWeek = d,
                DayName = dayNames[d],
                Count = dayMap.TryGetValue(d, out var c) ? c : 0
            })
            // Lundi en premier
            .OrderBy(d => (d.DayOfWeek + 6) % 7)
            .ToList();

        // --- Top 10 processus visés ---
        stats.TopProcesses = validAlerts
            .Where(e => !string.IsNullOrWhiteSpace(e.ProcessName) && e.ProcessName != "Network Packet")
            .GroupBy(e => e.ProcessName!)
            .Select(g => new ProcessStats
            {
                ProcessName = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(p => p.Count)
            .Take(10)
            .ToList();

        // --- Distribution des sévérités (toujours 4 entrées, y compris à 0) ---
        var sevOrder = new[]
        {
            Severity.Critical, Severity.High, Severity.Warning,
            Severity.Info, Severity.Safe, Severity.Trusted, Severity.Whitelisted
        };
        stats.SeverityDistribution = sevOrder
            .Select(s => new SeverityStat
            {
                Severity = s.ToString(),
                Count = validAlerts.Count(e => e.Severity == s)
            })
            .Where(s => s.Count > 0)   // ? filtre les niveaux à 0
            .ToList();

        // --- Top détecteurs globaux ---
        stats.TopDetectors = validAlerts
            .GroupBy(e => e.Source)
            .Select(g => new DetectorStats { Detector = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(15)
            .ToList();

        return stats;
    }

    // -------------------------------------------------------------
    //  Bloc à partir d'une liste d'IntrusionEvent (actives)
    // -------------------------------------------------------------
    private static async Task<StatsBlock> BuildBlockFromEventsAsync(
        List<IntrusionEvent> events,
        GeoIpService geo,
        int topIpsLimit, int topCountriesLimit, int topDetectorsLimit)
    {
        var block = new StatsBlock
        {
            TotalAlerts = events.Count,
            DistinctIps = events.Select(e => e.RemoteIp).Distinct().Count()
        };

        // --- Pré-résolution GeoIP (cache local pour éviter les appels en double) ---
        var geoMap = new Dictionary<string, GeoInfo>();
        foreach (var ip in events.Select(e => e.RemoteIp!).Distinct())
        {
            var gi = geo.TryGetCached(ip) ?? await geo.ResolveAsync(ip);
            if (gi != null) geoMap[ip] = gi;
        }

        // --- Top IPs ---
        var byIp = events
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
            geoMap.TryGetValue(item.Ip, out var gi);
            ipStatsList.Add(new IpStats
            {
                Ip = item.Ip,
                Count = item.Count,
                FirstSeen = item.FirstSeen,
                LastSeen = item.LastSeen,
                MaxSeverity = item.MaxSeverity,
                Detectors = item.Detectors,
                Country = gi?.Country,
                CountryCode = gi?.CountryCode,
                City = gi?.City,
                Isp = gi?.Isp,
                ReverseDns = gi?.ReverseDns,
                IsPrivate = gi?.IsPrivate ?? false
            });
        }
        block.TopIps = ipStatsList;

        // --- Pays ---
        block.TopCountries = ipStatsList
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

        block.DistinctCountries = geoMap.Values
            .Where(g => !g.IsPrivate && !string.IsNullOrEmpty(g.CountryCode))
            .Select(g => g.CountryCode)
            .Distinct()
            .Count();

        // --- Détecteurs ---
        block.TopDetectors = events
            .GroupBy(e => e.Source)
            .Select(g => new DetectorStats { Detector = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(topDetectorsLimit)
            .ToList();

        // --- Worst severity ---
        block.WorstSeverity = events.Any(e => e.Severity == Severity.Critical) ? "critical"
                            : events.Any(e => e.Severity == Severity.High ||
                                              e.Severity == Severity.Warning) ? "warning"
                            : "ok";

        // --- Map data : alertes par pays (sur TOUTES les alertes, pas seulement le top) ---
        var countryMap = new Dictionary<string, int>();
        foreach (var ev in events)
        {
            if (!geoMap.TryGetValue(ev.RemoteIp!, out var gi)) continue;
            if (gi.IsPrivate || string.IsNullOrEmpty(gi.CountryCode)) continue;
            countryMap[gi.CountryCode] = countryMap.GetValueOrDefault(gi.CountryCode) + 1;
        }
        block.AlertsByCountryCode = countryMap;

        return block;
    }

    // -------------------------------------------------------------
    //  Bloc à partir d'une liste de BlockedIpInfo (bloquées)
    //  ? Lecture pure des champs persistés, zéro appel API.
    // -------------------------------------------------------------
    private static Task<StatsBlock> BuildBlockFromBlockedAsync(
        List<BlockedIpInfo> blockedList,
        GeoIpService geo,
        int topIpsLimit, int topCountriesLimit, int topDetectorsLimit)
    {
        // Double sécurité : si l'appelant n'a pas filtré, on le fait ici.
        blockedList = blockedList
            .Where(b => !b.IsProcess && !string.IsNullOrWhiteSpace(b.Ip))
            .ToList();

        var block = new StatsBlock
        {
            TotalAlerts = blockedList.Count,
            DistinctIps = blockedList.Count,
            WorstSeverity = "blocked"
        };

        // --- Top IPs : lecture directe des champs GeoIP persistés ---
        var ipStatsList = blockedList
            .Select(b => new IpStats
            {
                Ip = b.Ip,
                Count = 1,
                FirstSeen = b.BlockedAt,
                LastSeen = b.BlockedAt,
                MaxSeverity = "blocked",
                Detectors = new List<string> { b.Source },
                Country = b.Country,
                CountryCode = b.CountryCode,
                City = b.City,
                Isp = b.Isp,
                ReverseDns = b.ReverseDns,
                IsPrivate = b.IsPrivate
            })
            .OrderBy(x => x.Country ?? "")
            .ThenBy(x => x.Ip)
            .Take(topIpsLimit)
            .ToList();
        block.TopIps = ipStatsList;

        // --- Pays : agrégation sur TOUTES les bloquées (pas seulement le top) ---
        block.TopCountries = blockedList
            .Where(b => !b.IsPrivate && !string.IsNullOrEmpty(b.Country))
            .GroupBy(b => new { b.Country, b.CountryCode })
            .Select(g => new CountryStats
            {
                Country = g.Key.Country ?? "",
                CountryCode = g.Key.CountryCode ?? "",
                Count = g.Count(),
                DistinctIps = g.Select(b => b.Ip).Distinct().Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(topCountriesLimit)
            .ToList();

        block.DistinctCountries = blockedList
            .Where(b => !b.IsPrivate && !string.IsNullOrEmpty(b.CountryCode))
            .Select(b => b.CountryCode!)
            .Distinct()
            .Count();

        // --- Détecteurs ---
        block.TopDetectors = blockedList
            .GroupBy(b => b.Source)
            .Select(g => new DetectorStats { Detector = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(topDetectorsLimit)
            .ToList();

        // --- Map data : alertes par pays (sur TOUTES les bloquées) ---
        var countryMap = new Dictionary<string, int>();
        foreach (var b in blockedList)
        {
            if (b.IsPrivate || string.IsNullOrEmpty(b.CountryCode)) continue;
            countryMap[b.CountryCode] = countryMap.GetValueOrDefault(b.CountryCode) + 1;
        }
        block.AlertsByCountryCode = countryMap;

        return Task.FromResult(block);
    }
}