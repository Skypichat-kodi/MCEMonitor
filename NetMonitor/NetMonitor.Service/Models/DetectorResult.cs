namespace NetMonitor.Service.Models;

public enum Severity
{
    Whitelisted = 0,   // Override manuel              (blanc)
    Trusted     = 1,   // Scanner de recherche         (jaune)
    Safe        = 2,   // Microsoft vérifié            (vert)
    Info        = 3,   // Legacy (affiché comme Warning)
    Crawler     = 4,   // Robot générique (Yandex...)  (bleu foncé)
    Bot         = 5,   // Robot officiel (Google)      (bleu clair)
    ToCheck     = 6,   // À vérifier                   (magenta)
    Warning     = 7,   // Attention                    (orange)
    High        = 8,   // Legacy (affiché comme Critical)
    Critical    = 9    // Malveillant avéré            (rouge foncé)
}

public sealed class DetectorResult
{
    public bool IsThreat            { get; init; }
    public Severity Severity        { get; init; } = Severity.Info;
    public string Message           { get; init; } = "";
    public string? Details          { get; init; }
    public string? ProcessPath      { get; init; }
    public string? TrustReason      { get; init; }
    public bool IsPendingProcess    { get; init; }
}