using NetMonitor.Service.Models;
using NetMonitor.Service.Services;

namespace NetMonitor.Service.Detectors;

public sealed class BlacklistIpDetector : IConnectionAwareDetector, IDetector
{
    public string Name => "Blacklist";
    private readonly ThreatIntelService _threatIntel;

    public BlacklistIpDetector(ThreatIntelService threatIntel)
    {
        _threatIntel = threatIntel;
    }

    // Appelé par ETW (Connexions établies)
    public DetectorResult? InspectConnection(ConnectionInfo ci, DetectorContext ctx)
    {
        if (_threatIntel.IsBlacklisted(ci.RemoteIp))
        {
            return new DetectorResult
            {
                IsThreat = true,
                Severity = Severity.Critical,
                Message = $"Connexion vers une IP blacklistée : {ci.RemoteIp}",
                Details = $"Processus : {ci.ProcessName} (PID {ci.Pid}) — Port distant : {ci.RemotePort}"
            };
        }
        return null;
    }

    // Appelé par WinDivert (Paquets bruts)
    public DetectorResult? Inspect(NetworkEvent evt, DetectorContext ctx)
    {
        // Si l'IP distante (source ou destination) est dans la blacklist
        if (_threatIntel.IsBlacklisted(evt.SourceIp))
        {
            return new DetectorResult
            {
                IsThreat = true,
                Severity = Severity.Critical,
                Message = $"Trafic entrant depuis une IP blacklistée : {evt.SourceIp}",
                Details = $"Port source : {evt.SourcePort} -> Port local : {evt.DestPort}"
            };
        }

        if (_threatIntel.IsBlacklisted(evt.DestIp))
        {
            return new DetectorResult
            {
                IsThreat = true,
                Severity = Severity.Critical,
                Message = $"Trafic sortant vers une IP blacklistée : {evt.DestIp}",
                Details = $"Port local : {evt.SourcePort} -> Port distant : {evt.DestPort}"
            };
        }
        return null;
    }
}