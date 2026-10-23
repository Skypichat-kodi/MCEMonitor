using System;
using System.Collections.Generic;

namespace NetMonitor.Service.Models;

public sealed class IpStats
{
    public string Ip { get; set; } = "";
    public int Count { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public string MaxSeverity { get; set; } = "ok";
    public List<string> Detectors { get; set; } = new();

    public string? Country { get; set; }
    public string? CountryCode { get; set; }
    public string? City { get; set; }
    public string? Isp { get; set; }
    public string? ReverseDns { get; set; }
    public bool IsPrivate { get; set; }
}

public sealed class CountryStats
{
    public string Country { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public int Count { get; set; }
    public int DistinctIps { get; set; }
}

public sealed class DetectorStats
{
    public string Detector { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>
/// Bloc de statistiques pour un groupe (actives OU bloquées).
/// </summary>
public sealed class StatsBlock
{
    public int TotalAlerts { get; set; }
    public int DistinctIps { get; set; }
    public int DistinctCountries { get; set; }
    public string WorstSeverity { get; set; } = "ok";
    public List<IpStats> TopIps { get; set; } = new();
    public List<CountryStats> TopCountries { get; set; } = new();
    public List<DetectorStats> TopDetectors { get; set; } = new();
    public Dictionary<string, int> AlertsByCountryCode { get; set; } = new();
}

public sealed class GlobalStats
{
    // --- Résumé global (toutes alertes) ---
    public int TotalAlerts { get; set; }
    public int DistinctIps { get; set; }
    public int DistinctCountries { get; set; }
    public string WorstSeverity { get; set; } = "ok";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, int> SeverityCounts { get; set; } = new();
    public List<PortStats> TopLocalPorts { get; set; } = new();
    public List<PortStats> TopRemotePorts { get; set; } = new();
    public List<HourlyStats> ActivityByHour { get; set; } = new();
    public List<DailyStats> ActivityByDay { get; set; } = new();
    public List<ProcessStats> TopProcesses { get; set; } = new();
    public List<SeverityStat> SeverityDistribution { get; set; } = new();
    public List<DetectorStats> TopDetectors { get; set; } = new();    

    // --- Blocs détaillés ---
    public StatsBlock Active { get; set; } = new();
    public StatsBlock Blocked { get; set; } = new();
}
public sealed class PortStats
{
    public int Port { get; set; }
    public int Count { get; set; }
    public List<string> TopDetectors { get; set; } = new();
}

public sealed class HourlyStats
{
    public int Hour { get; set; }
    public int Count { get; set; }
    public int CriticalCount { get; set; }
}

public sealed class DailyStats
{
    public int DayOfWeek { get; set; }
    public string DayName { get; set; } = "";
    public int Count { get; set; }
}

public sealed class ProcessStats
{
    public string ProcessName { get; set; } = "";
    public int Count { get; set; }
}

public sealed class SeverityStat
{
    public string Severity { get; set; } = "";
    public int Count { get; set; }
}