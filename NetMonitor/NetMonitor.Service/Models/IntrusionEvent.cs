namespace NetMonitor.Service.Models;

public sealed class IntrusionEvent
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = "";        // nom du détecteur
    public Severity Severity { get; set; }
    public string Message { get; set; } = "";
    public string? RemoteIp { get; set; }
    public int RemotePort { get; set; }
    public int LocalPort { get; set; }
    public int Pid { get; set; }
    public string? ProcessName { get; set; }
    public string? Details { get; set; }
}