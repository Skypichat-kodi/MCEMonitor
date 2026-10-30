namespace NetMonitor.Service.Models;

/// <summary>
/// Niveaux de sévérité, ordonnés du plus faible au plus fort.
///
/// ⚠ L'ordre est IMPORTANT :
///   - (int)Severity.Critical doit être le plus grand (utilisé par les tris)
///   - (int)Severity.Whitelisted doit être le plus petit
///
/// Les valeurs existantes (Info, Warning, High, Critical) ont été conservées
/// mais leur valeur numérique a changé (Info était 0, il est maintenant 3).
/// Impact : transparent car la sérialisation JSON utilise JsonStringEnumConverter.
/// </summary>
public enum Severity
{
    Whitelisted = 0,   // Override manuel utilisateur (vert foncé)
    Trusted     = 1,   // Scanner de recherche connu    (vert moyen)
    Safe        = 2,   // Domaine légitime connu        (vert clair)
    Info        = 3,   // Niveau d'information (bas)
    Warning     = 4,   // À vérifier                    (orange)
    High        = 5,   // Élevé (legacy, conservé)
    Critical    = 6    // Vraiment malveillant          (rouge)
}

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