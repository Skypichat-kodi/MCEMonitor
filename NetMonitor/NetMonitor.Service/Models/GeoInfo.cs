namespace NetMonitor.Service.Models;

public sealed class GeoInfo
{
    public string Ip { get; set; } = "";
    public string Country { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string City { get; set; } = "";
    public string Isp { get; set; } = "";
    public string Org { get; set; } = "";
    public bool IsPrivate { get; set; }
    public string? ReverseDns { get; set; }
    public DateTime ResolvedAt { get; set; }

    public static GeoInfo Private(string ip) => new()
    {
        Ip = ip,
        Country = "Réseau local",
        CountryCode = "LAN",
        IsPrivate = true,
        ResolvedAt = DateTime.UtcNow
    };
}