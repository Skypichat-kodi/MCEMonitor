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

public sealed class GlobalStats
{
    public int TotalAlerts { get; set; }
    public int DistinctIps { get; set; }
    public int DistinctCountries { get; set; }
    public string WorstSeverity { get; set; } = "ok";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<IpStats> TopIps { get; set; } = new();
    public List<CountryStats> TopCountries { get; set; } = new();
    public List<DetectorStats> TopDetectors { get; set; } = new();
    public Dictionary<string, int> SeverityCounts { get; set; } = new();
    public Dictionary<string, int> AlertsByCountryCodeActive { get; set; } = new();
    public Dictionary<string, int> AlertsByCountryCodeBlocked { get; set; } = new();
}