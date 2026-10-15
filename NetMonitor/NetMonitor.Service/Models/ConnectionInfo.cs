namespace NetMonitor.Service.Models;

public sealed class ConnectionInfo
{
    public int Pid { get; set; }
    public string? ProcessName { get; set; }
    public string LocalIp { get; set; } = "";
    public int LocalPort { get; set; }
    public string RemoteIp { get; set; } = "";
    public int RemotePort { get; set; }
    public TransportProtocol Protocol { get; set; } = TransportProtocol.Tcp;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum TransportProtocol { Tcp, Udp }