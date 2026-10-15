using NetMonitor.Service.Models;

namespace NetMonitor.Service.Detectors;

public sealed class PortScanDetector : IConnectionAwareDetector
{
    public string Name => "PortScan";

    public DetectorResult? Inspect(NetworkEvent evt, DetectorContext ctx) => null;

    public DetectorResult? InspectConnection(ConnectionInfo ci, DetectorContext ctx)
    {
        // On ne considère que du TCP sortant (tentatives d'ouverture)
        if (ci.Protocol != TransportProtocol.Tcp) return null;

        // Ignorer les ports de service très fréquents (DNS, NTP, HTTPS, HTTP)
        if (ctx.Settings.IgnoredRemotePorts.Contains(ci.RemotePort)) return null;

        var window = TimeSpan.FromSeconds(ctx.Settings.PortScanWindowSeconds);
        var ports = ctx.RecordSynPort(ci.RemoteIp, ci.RemotePort, window);

        // Seuil : nombre de ports *distincts* atteints dans la fenêtre
        var distinct = ports.Distinct().Count();
        if (distinct < ctx.Settings.PortScanThreshold) return null;

        return new DetectorResult
        {
            IsThreat = true,
            Severity = Severity.High,
            Message = $"Scan de ports détecté depuis {ci.RemoteIp} " +
                      $"({distinct} ports en {ctx.Settings.PortScanWindowSeconds} s)",
            Details = $"PID initiateur : {ci.Pid} ({ci.ProcessName ?? "?"}) — " +
                      $"ports : {string.Join(", ", ports.Distinct().OrderBy(p => p).Take(40))}"
        };
    }
}