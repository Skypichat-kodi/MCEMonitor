using NetMonitor.Service.Models;

namespace NetMonitor.Service.Detectors;

public sealed class IcmpFloodDetector : IDetector
{
    public string Name => "IcmpFlood";

    private const int WindowSeconds = 1;
    private const int Threshold = 20;  // 20 pings par seconde

    public DetectorResult? Inspect(NetworkEvent evt, DetectorContext ctx)
    {
        if (evt.Protocol != TransportProtocol.Icmp) return null;
        if (evt.IcmpType != 8) return null;

        if (ctx.IsIgnoredNetwork(evt.SourceIp) || ctx.IsIgnoredNetwork(evt.DestIp)) 
            return null;

        if (evt.SourceIp == evt.DestIp) return null;

        var window = TimeSpan.FromSeconds(WindowSeconds);
        int count = ctx.RecordIcmpPacket(evt.SourceIp, window);

        // LOG DE DIAGNOSTIC
        CoreLog.Debug($"[IcmpFlood] ip={evt.SourceIp} dest={evt.DestIp} count={count}/{Threshold}");

        if (count < Threshold) return null;

        if (!ctx.TryAcquireAlertSlot($"icmpflood-{evt.SourceIp}", window))
            return null;

        return new DetectorResult
        {
            IsThreat = true,
            Severity = Severity.High,
            Message = $"ICMP Flood détecté depuis {evt.SourceIp} " +
                      $"({count} Echo Request en {WindowSeconds} s)",
            Details = $"Cible : {evt.DestIp}"
        };
    }
}