using NetMonitor.Service.Models;

namespace NetMonitor.Service.Detectors;

public sealed class SynFloodDetector : IDetector
{
    public string Name => "SynFlood";

    private const int WindowSeconds = 2;
    private const int Threshold = 50;

    private const byte SYN = 0x02;
    private const byte ACK = 0x10;

    public DetectorResult? Inspect(NetworkEvent evt, DetectorContext ctx)
    {
        if (evt.Protocol != TransportProtocol.Tcp) return null;

        // Détection d'un SYN pur (SYN=1, ACK=0)
        if ((evt.TcpFlags & SYN) == 0 || (evt.TcpFlags & ACK) != 0) return null;

        // NOUVEAU : Ignore le trafic local (pas d'alerte si ça vient du réseau interne)
        if (ctx.IsIgnoredNetwork(evt.SourceIp)) return null;

        // Ignore la boucle locale
        if (evt.SourceIp == evt.DestIp) return null;

        var window = TimeSpan.FromSeconds(WindowSeconds);
        int count = ctx.RecordSynPacket(evt.SourceIp, window);

        // Log de debug (activé avec MCEMONITOR_VERBOSE=1)
        CoreLog.Debug($"[SynFlood] ip={evt.SourceIp} port={evt.DestPort} " +
                      $"flags=0x{evt.TcpFlags:X2} count={count}/{Threshold}");

        if (count < Threshold) return null;

        // Anti-spam : une alerte par IP par fenêtre
        if (!ctx.TryAcquireAlertSlot($"synflood-{evt.SourceIp}", window))
            return null;

        return new DetectorResult
        {
            IsThreat = true,
            Severity = Severity.High,
            Message = $"SYN Flood détecté depuis {evt.SourceIp} " +
                      $"({count} SYN en {WindowSeconds} s)",
            Details = $"Cible : {evt.DestIp}:{evt.DestPort}"
        };
    }
}