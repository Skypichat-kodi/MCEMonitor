using NetMonitor.Service.Models;
using NetMonitor.Service.Services;

namespace NetMonitor.Service.Detectors;

/// <summary>
/// Alerte sur toute connexion sortante initiée par un processus non approuvé
/// (nom absent de la whitelist, ou chemin/hash/signature différent).
/// Severity Info par défaut, escalade en High si Spoofed.
/// </summary>
public sealed class UntrustedProcessDetector : IConnectionAwareDetector
{
    public string Name => "UntrustedProcess";

    private readonly ProcessTrustService _trust;

    public UntrustedProcessDetector(ProcessTrustService trust)
    {
        _trust = trust;
    }

    public DetectorResult? Inspect(NetworkEvent evt, DetectorContext ctx) => null;

    public DetectorResult? InspectConnection(ConnectionInfo ci, DetectorContext ctx)
    {
        // Ignore le trafic local (pas de détection pour les connexions internes)
        if (ctx.IsIgnoredNetwork(ci.RemoteIp)) return null;

        // Ignore les processus système connus
        if (ci.Pid <= 4) return null;

        var result = _trust.CheckProcess(ci.Pid);

        // Processus approuvé ? rien à signaler
        if (result.Level == ProcessTrustLevel.Trusted || result.Level == ProcessTrustLevel.System)
            return null;

        // Usurpation (nom whitelisté mais chemin/hash/signature différent) ? High
        if (result.Level == ProcessTrustLevel.Spoofed)
        {
            // Cas Spoofed (High)
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

        // Cas Unknown (Info)
        return new DetectorResult
        {
            IsThreat = true,
            IsPendingProcess = true,
            Severity = Severity.Warning,   // ? Warning au lieu de Info
            Message = $"Connexion sortante d'un processus non approuvé : {result.ProcessName} " +
                      $"vers {ci.RemoteIp}:{ci.RemotePort}",
            ProcessPath = result.FullPath,
            TrustReason = result.Reason,
            Details = null
        };
    }
}