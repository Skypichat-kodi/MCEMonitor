namespace NetMonitor.Service.Models;

public enum Severity { Info, Warning, High, Critical }

public sealed class DetectorResult
{
    public bool IsThreat { get; init; }
    public Severity Severity { get; init; } = Severity.Info;
    public string Message { get; init; } = "";
    public string? Details { get; init; }
    public string? ProcessPath { get; init; }
    public string? TrustReason { get; init; }
    public bool IsPendingProcess { get; init; }
}