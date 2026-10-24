namespace NetMonitor.Service.Models;

/// <summary>
/// Processus détecté mais pas encore traité par l'utilisateur
/// (ni whitelisté, ni bloqué, ni ignoré).
/// Clé de déduplication = ProcessPath (le PID change à chaque lancement).
/// </summary>
public sealed class PendingProcessInfo
{
    public string ProcessPath { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string? TrustReason { get; set; }
    public string? RemoteIp { get; set; }
    public int RemotePort { get; set; }
    public int Pid { get; set; }
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public int Count { get; set; } = 1;
    public Severity Severity { get; set; } = Severity.Info;
    public string? Detector { get; set; }
}