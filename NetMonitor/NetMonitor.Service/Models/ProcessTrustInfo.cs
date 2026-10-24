namespace NetMonitor.Service.Models;

/// <summary>
/// Entrée de la whitelist des processus approuvés.
/// </summary>
public sealed class ProcessTrustInfo
{
    /// <summary>Nom du processus (ex: "chrome").</summary>
    public string Name { get; set; } = "";

    /// <summary>Chemin complet attendu (ex: C:\Program Files\Google\Chrome\Application\chrome.exe).</summary>
    public string Path { get; set; } = "";

    /// <summary>Hash SHA-256 du binaire (hex minuscule). Vide = non vérifié.</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>Éditeur attendu (subject Authenticode). Vide = non vérifié.</summary>
    public string Publisher { get; set; } = "";

    /// <summary>Si false, l'entrée est ignorée (permet de désactiver une règle sans la supprimer).</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Résultat de la vérification d'un processus.
/// </summary>
public enum ProcessTrustLevel
{
    /// <summary>Processus approuvé (nom + chemin + hash/signature OK).</summary>
    Trusted,

    /// <summary>Processus inconnu (pas dans la whitelist).</summary>
    Unknown,

    /// <summary>Processus avec un nom whitelisté mais chemin/hash différents ? tentative d'usurpation.</summary>
    Spoofed,

    /// <summary>Processus système critique (PID 4, 0).</summary>
    System
}

public sealed class ProcessTrustResult
{
    public ProcessTrustLevel Level { get; init; }
    public string ProcessName { get; init; } = "";
    public string? FullPath { get; init; }
    public string? Sha256 { get; init; }
    public string? Publisher { get; init; }
    public string? MatchedEntry { get; init; }  // nom de l'entrée whitelist qui a matché
    public string? Reason { get; init; }        // raison du spoof / unknown
}