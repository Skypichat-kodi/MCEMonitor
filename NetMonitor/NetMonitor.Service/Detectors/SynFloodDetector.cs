using NetMonitor.Service.Models;

namespace NetMonitor.Service.Detectors;

public sealed class SynFloodDetector : IDetector
{
    public string Name => "SynFlood";

    private const int WindowSeconds = 2;      // ? Élargi à 2 s
    private const int Threshold = 50;         // ? Baissé à 50 pour le test

    public DetectorResult? Inspect(NetworkEvent evt, DetectorContext ctx)
    {
        if (evt.Protocol != TransportProtocol.Tcp) return null;

        const byte SYN = 0x02;
        const byte ACK = 0x10;
        if ((evt.TcpFlags & SYN) == 0 || (evt.TcpFlags & ACK) != 0) return null;

        if (evt.SourceIp == evt.DestIp) return null;

        var window = TimeSpan.FromSeconds(WindowSeconds);
        int count = ctx.RecordSynPacket(evt.SourceIp, window);

        CoreLog.Debug($"[SynFlood] ip={evt.SourceIp} port={evt.DestPort} " +
                      $"flags=0x{evt.TcpFlags:X2} count={count}/{Threshold}");

        if (count < Threshold) return null;

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