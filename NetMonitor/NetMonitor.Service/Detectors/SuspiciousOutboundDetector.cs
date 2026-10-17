using NetMonitor.Service.Models;

namespace NetMonitor.Service.Detectors;

public sealed class SuspiciousOutboundDetector : IConnectionAwareDetector
{
    public string Name => "SuspiciousOutbound";

    public DetectorResult? InspectConnection(ConnectionInfo ci, DetectorContext ctx)
    {
        // On ne regarde que les connexions établies (ETW) pour identifier le processus
        if (ctx.Settings.SuspiciousRemotePorts.Contains(ci.RemotePort))
        {
            // Ignore si c'est une IP locale (pour éviter les faux positifs internes)
            if (ctx.IsIgnoredNetwork(ci.RemoteIp)) return null;

            return new DetectorResult
            {
                IsThreat = true,
                Severity = Severity.High,
                Message = $"Connexion sortante vers un port suspect ({ci.RemotePort}) depuis {ci.ProcessName}",
                Details = $"Cible : {ci.RemoteIp}:{ci.RemotePort} (PID {ci.Pid})"
            };
        }

        return null;
    }

    public DetectorResult? Inspect(NetworkEvent evt, DetectorContext ctx) => null;
}