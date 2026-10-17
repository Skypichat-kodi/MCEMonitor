namespace NetMonitor.Service.Models;

public sealed class NetworkEvent
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TransportProtocol Protocol { get; set; }
    
    public string SourceIp { get; set; } = "";
    public string DestIp { get; set; } = "";
    
    public int SourcePort { get; set; }
    public int DestPort { get; set; }
    
    public string? SourceMac { get; set; } // Pour ARP Spoofing
    public byte TcpFlags { get; set; }     // Pour SYN Flood
    public byte IcmpType { get; set; }     // Pour ICMP Flood
    public byte IcmpCode { get; set; }
    
    public int Length { get; set; }
    public byte[]? Payload { get; set; }   // Pour DNS Spoofing
}