using NetMonitor.Service.Models;

namespace NetMonitor.Service.Detectors;

public sealed class PortScanDetector : IConnectionAwareDetector
{
    public string Name => "PortScan";

    public DetectorResult? Inspect(NetworkEvent evt, DetectorContext ctx) => null;

    public DetectorResult? InspectConnection(ConnectionInfo ci, DetectorContext ctx)
    {
        if (ci.Protocol != TransportProtocol.Tcp) return null;
        if (ctx.Settings.IgnoredRemotePorts.Contains(ci.RemotePort)) return null;

        var window = TimeSpan.FromSeconds(ctx.Settings.PortScanWindowSeconds);
        var ports = ctx.RecordSynPort(ci.RemoteIp, ci.RemotePort, window);
        var distinct = ports.Distinct().Count();

        CoreLog.Debug($"[PortScan] ip={ci.RemoteIp} port={ci.RemotePort} distinct={distinct}/{ctx.Settings.PortScanThreshold}");

        if (distinct < ctx.Settings.PortScanThreshold) return null;

        // Anti-spam : une seule alerte par IP par fenêtre
        var cooldown = TimeSpan.FromSeconds(ctx.Settings.PortScanWindowSeconds * 2);
        if (!ctx.TryAcquireAlertSlot(ci.RemoteIp, cooldown))
            return null;

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