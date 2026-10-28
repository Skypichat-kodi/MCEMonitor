using System;
using System.Linq;
using NetMonitor.Service.Models;
using NetMonitor.Service.Services;

namespace NetMonitor.Service.Detectors;

/// <summary>
/// Alerte sur toute connexion sortante initiée par un processus non approuvé
/// (nom absent de la whitelist, ou chemin/hash/signature différent).
///
/// Règles de sévérité :
///   - Spoofed (nom whitelisté mais chemin/hash/signature différent) → High
///   - Unknown + chemin inaccessible (ERROR_ACCESS_DENIED) + nom inconnu → High
///   - Unknown + chemin inaccessible sur processus système connu → Info (bénin)
///   - Unknown + PID mort / erreur transitoire → Info (bruit)
///   - Unknown sans autre info → Warning
/// </summary>
public sealed class UntrustedProcessDetector : IConnectionAwareDetector
{
    public string Name => "UntrustedProcess";

    private readonly ProcessTrustService _trust;

    // Processus système connus qui peuvent légitimement être inaccessibles (PPL)
    // ⚠ Doit rester synchronisé avec la liste de ProcessTrustService
    private static readonly string[] KnownProtectedProcesses =
    {
        "msmpeng", "lsass", "services", "wininit", "smss", "csrss",
        "securityhealthservice", "securityhealthsystray",
        "registry", "memcompression"
    };

    // Préfixe des chemins considérés comme "réseau local" (déjà filtré par ctx.IsIgnoredNetwork,
    // mais on garde une ceinture de sécurité)
    private static readonly string[] LocalNetworkPrefixes =
    {
        "127.", "10.", "192.168.", "169.254.", "::1", "fe80:"
    };

    public UntrustedProcessDetector(ProcessTrustService trust)
    {
        _trust = trust;
    }

    public DetectorResult? Inspect(NetworkEvent evt, DetectorContext ctx) => null;

    public DetectorResult? InspectConnection(ConnectionInfo ci, DetectorContext ctx)
    {
        // Ignore le trafic local
        if (ctx.IsIgnoredNetwork(ci.RemoteIp)) return null;

        // Ignore les processus système "triviaux"
        if (ci.Pid <= 4) return null;

        var result = _trust.CheckProcess(ci.Pid);

        // Processus approuvé ? rien à signaler
        if (result.Level == ProcessTrustLevel.Trusted || result.Level == ProcessTrustLevel.System)
            return null;

        // ============================================================
        //  CAS 1 : SPOOFED → toujours High
        // ============================================================
        if (result.Level == ProcessTrustLevel.Spoofed)
        {
            return new DetectorResult
            {
                IsThreat = true,
                IsPendingProcess = true,
                Severity = Severity.High,
                Message = $"Processus suspect (usurpation probable) : {result.ProcessName} " +
                          $"vers {ci.RemoteIp}:{ci.RemotePort}",
                ProcessPath = result.FullPath,
                TrustReason = result.Reason,
                Details = null
            };
        }

        // ============================================================
        //  CAS 2 : UNKNOWN → escalade conditionnelle
        // ============================================================
        // On distingue plusieurs sous-cas via le Reason + Win32Error remontés par ProcessTrustService.

        bool pathInaccessible = IsPathInaccessible(result);
        bool processKnownProtected = IsKnownProtectedProcess(result.ProcessName);
        bool remoteIsPublic = IsPublicRemote(ci.RemoteIp);
        bool pidLikelyDead = IsPidLikelyDead(result);

        // 2a. PID mort / erreur transitoire → bruit, on ne signale pas
        if (pidLikelyDead)
        {
            // On ne signale même pas : le processus n'existe plus, il n'y a rien à examiner.
            // Le tracker de pending s'en occupe (TTL). Retourner null évite de polluer l'UI.
            return null;
        }

        // 2b. Processus protégé système connu (MsMpEng, lsass...) → bénin, pas d'alerte
        if (pathInaccessible && processKnownProtected)
        {
            // On ne signale pas : c'est un PPL légitime. On pourrait retourner un Info
            // mais ça pollue l'UI pour rien. Le tracker de pending le verra si besoin.
            return null;
        }

        // 2c. Chemin inaccessible + processus inconnu + IP publique → SUSPECT
        if (pathInaccessible && !processKnownProtected && remoteIsPublic)
        {
            return new DetectorResult
            {
                IsThreat = true,
                IsPendingProcess = true,
                Severity = Severity.High,
                Message = $"Processus protégé non identifié : {result.ProcessName} " +
                          $"vers {ci.RemoteIp}:{ci.RemotePort} " +
                          $"(chemin inaccessible, PID {ci.Pid})",
                ProcessPath = result.FullPath,
                TrustReason = result.Reason,
                Details = $"Win32Error={result.Win32Error}, " +
                          $"nom='{result.ProcessName}', " +
                          $"raison='{result.Reason}'"
            };
        }

        // 2d. Chemin inaccessible + processus inconnu + IP privée → Warning
        // (cas rare : le réseau est peut-être mal configuré dans les ignoredNetworks)
        if (pathInaccessible && !processKnownProtected)
        {
            return new DetectorResult
            {
                IsThreat = true,
                IsPendingProcess = true,
                Severity = Severity.Warning,
                Message = $"Processus protégé non identifié : {result.ProcessName} " +
                          $"vers {ci.RemoteIp}:{ci.RemotePort}",
                ProcessPath = result.FullPath,
                TrustReason = result.Reason,
                Details = $"Win32Error={result.Win32Error}"
            };
        }

        // 2e. Cas Unknown générique (nom absent de la whitelist, chemin accessible)
        return new DetectorResult
        {
            IsThreat = true,
            IsPendingProcess = true,
            Severity = Severity.Warning,
            Message = $"Connexion sortante d'un processus non approuvé : {result.ProcessName} " +
                      $"vers {ci.RemoteIp}:{ci.RemotePort}",
            ProcessPath = result.FullPath,
            TrustReason = result.Reason,
            Details = null
        };
    }

    // ============================================================
    //  Helpers de classification
    // ============================================================

    /// <summary>
    /// Vrai si le Reason indique que le chemin n'a pas pu être résolu
    /// (OpenProcess refusé, ou processus mort entre-temps).
    /// </summary>
    private static bool IsPathInaccessible(ProcessTrustResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Reason)) return false;
        return result.Reason.Contains("inaccessible", StringComparison.OrdinalIgnoreCase)
            || result.Reason.Contains("droits", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Vrai si le nom du processus correspond à un processus système protégé connu.
    /// Utilise une comparaison insensible à la casse et tolère le ".exe" optionnel.
    /// </summary>
    private static bool IsKnownProtectedProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        // Retire l'extension si présente
        string name = processName;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return KnownProtectedProcesses.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Vrai si le Win32Error indique que le PID n'existe plus
    /// (ERROR_INVALID_PARAMETER = 87).
    /// </summary>
    private static bool IsPidLikelyDead(ProcessTrustResult result)
    {
        // Si ProcessTrustService a remonté Win32Error, on l'utilise.
        // ERROR_INVALID_PARAMETER (87) = le PID n'existe plus.
        // ERROR_ACCESS_DENIED (5) = droits insuffisants (PPL).
        return result.Win32Error == 87;
    }

    /// <summary>
    /// Vrai si l'IP distante est publique (pas RFC1918, pas loopback, pas link-local).
    /// Ceinture de sécurité en plus de ctx.IsIgnoredNetwork.
    /// </summary>
    private static bool IsPublicRemote(string? remoteIp)
    {
        if (string.IsNullOrWhiteSpace(remoteIp)) return false;

        foreach (var prefix in LocalNetworkPrefixes)
        {
            if (remoteIp.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // 172.16.0.0/12 → 172.16.x.x à 172.31.x.x
        if (remoteIp.StartsWith("172.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = remoteIp.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int second))
            {
                if (second >= 16 && second <= 31) return false;
            }
        }

        return true;
    }
}