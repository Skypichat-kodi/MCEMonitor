using System.Text.Json.Serialization;

namespace NetMonitor.UI.Models;

public enum Severity { Info, Warning, High, Critical }

public sealed class AlertDto
{
    public DateTime Timestamp { get; set; }
    public Severity Severity { get; set; }
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Details { get; set; }

    [JsonIgnore]
    public string TimestampLocal => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    [JsonIgnore]
    public string SeverityText => Severity.ToString();
}

public sealed class ConnectionDto
{
    public int Pid { get; set; }
    public string? ProcessName { get; set; }
    public string LocalIp { get; set; } = "";
    public int LocalPort { get; set; }
    public string RemoteIp { get; set; } = "";
    public int RemotePort { get; set; }
    public string Protocol { get; set; } = "Tcp";
    public DateTime Timestamp { get; set; }

    [JsonIgnore]
    public string TimestampLocal => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    [JsonIgnore]
    public string Endpoint => $"{LocalPort} ? {RemoteIp}:{RemotePort}";
}