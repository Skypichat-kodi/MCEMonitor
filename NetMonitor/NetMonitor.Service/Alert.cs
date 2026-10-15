using NetMonitor.Service.Models;

namespace NetMonitor.Service;

public sealed class Alert
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Severity Severity { get; set; }
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Details { get; set; }
}