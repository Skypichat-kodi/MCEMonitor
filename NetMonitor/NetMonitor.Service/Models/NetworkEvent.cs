namespace NetMonitor.Service.Models;

/// <summary>
/// Représente un paquet réseau normalisé (utilisé par la Phase 2 WinDivert).
/// En Phase 1 (ETW), on travaille uniquement sur ConnectionInfo.
/// </summary>
public sealed class NetworkEvent
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TransportProtocol Protocol { get; set; }
    public string SourceIp { get; set; } = "";
    public int SourcePort { get; set; }
    public string DestIp { get; set; } = "";
    public int DestPort { get; set; }
    public int Length { get; set; }
    public byte[]? Payload { get; set; }
}