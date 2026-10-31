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
///   - Unknown + chemin inaccessible sur processus système connu → ignoré
///   - Unknown + PID mort / erreur transitoire → ignoré
///   - Unknown sans autre info → Warning
/// </summary>
public sealed class UntrustedProcessDetector : IConnectionAwareDetector
{
    public string Name => "UntrustedProcess";

    private readonly ProcessTrustService _trust;

    // Processus système connus qui peuvent légitimement être inaccessibles (PPL)
    private static readonly string[] KnownProtectedProcesses =
    {
        "msmpeng", "lsass", "services", "wininit", "smss", "csrss",
        "securityhealthservice", "securityhealthsystray",
        "registry", "memcompression"
    };

    // Préfixes réseau local
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
        //  CAS 2 : UNKNOWN → escalade conditionnelle via Win32Error
        // ============================================================
        bool remoteIsPublic = IsPublicRemote(ci.RemoteIp);
        bool processKnownProtected = IsKnownProtectedProcess(result.ProcessName);

        // 2a. PID mort (terminé entre-temps) → bruit, on ne signale pas
        if (result.Win32Error == 87) // ERROR_INVALID_PARAMETER
        {
            return null;
        }

        // 2b. Processus protégé système connu (MsMpEng, lsass...) → bénin
        if (result.Win32Error == 5 && processKnownProtected) // ERROR_ACCESS_DENIED
        {
            return null;
        }

        // 2c. Chemin inaccessible + processus inconnu + IP publique → SUSPECT
        if (result.Win32Error == 5 && !processKnownProtected && remoteIsPublic)
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
                Details = $"Win32Error={result.Win32Error}, nom='{result.ProcessName}'"
            };
        }

        // 2d. Chemin inaccessible + processus inconnu + IP privée → Warning
        if (result.Win32Error == 5 && !processKnownProtected)
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

        // 2e. Chemin vide + aucune erreur Win32 (race condition) → on ignore
        if (result.Win32Error == 0 && string.IsNullOrWhiteSpace(result.FullPath))
        {
            CoreLog.Debug($"[PENDING] Processus ignoré (chemin vide, PID probablement recyclé ou mort) : {result.ProcessName} (PID {ci.Pid})");
            return null;
        }

        // 2f. Cas Unknown générique (nom absent de la whitelist, chemin accessible)
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

    private static bool IsKnownProtectedProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        string name = processName;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return KnownProtectedProcesses.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsPublicRemote(string? remoteIp)
    {
        if (string.IsNullOrWhiteSpace(remoteIp)) return false;

        foreach (var prefix in LocalNetworkPrefixes)
        {
            if (remoteIp.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // 172.16.0.0/12
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