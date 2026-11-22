namespace NetMonitor.Service.Models;

/// <summary>
/// Un blocage qui ne serait plus décidé aujourd'hui : l'IP n'est plus dans la
/// blacklist et sa sévérité recalculée ne déclenche plus le blocage automatique
/// avec les réglages en vigueur.
/// </summary>
public sealed class ObsoleteBlock
{
    public string Ip     { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Source   { get; set; } = "";
    public string Reason   { get; set; } = "";
}

/// <summary>
/// Résultat d'une réanalyse des blocages. Les IP concernées ne sont débloquées
/// par personne : le service rapporte, l'utilisateur décide.
/// </summary>
public sealed class ReevaluationReport
{
    /// <summary>Nombre d'IP examinées (les blocages de processus sont exclus).</summary>
    public int Analysed        { get; set; }

    /// <summary>Nombre d'IP dont la sévérité ou l'origine a changé.</summary>
    public int Reclassified    { get; set; }

    /// <summary>Nombre d'IP dont la sévérité a été forcée à la main : jamais touchées.</summary>
    public int ManualOverrides { get; set; }

    /// <summary>Blocages qui ne seraient plus décidés aujourd'hui.</summary>
    public List<ObsoleteBlock> Obsolete { get; set; } = new();
}
