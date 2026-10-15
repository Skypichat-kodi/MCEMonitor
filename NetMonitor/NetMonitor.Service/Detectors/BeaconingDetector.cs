using NetMonitor.Service.Models;

namespace NetMonitor.Service.Detectors;

/// <summary>
/// Détecte des connexions périodiques régulières (C2 / beacon malware).
/// Critère : au moins N connexions d'un même process dans la fenêtre,
/// avec un écart-type faible entre les intervalles.
/// </summary>
public sealed class BeaconingDetector : IConnectionAwareDetector
{
    public string Name => "Beaconing";

    public DetectorResult? Inspect(NetworkEvent evt, DetectorContext ctx) => null;

    public DetectorResult? InspectConnection(ConnectionInfo ci, DetectorContext ctx)
    {
        if (ci.Pid <= 0) return null;
        if (ctx.Settings.IgnoredProcesses.Any(p =>
                string.Equals(p, ci.ProcessName, StringComparison.OrdinalIgnoreCase)))
            return null;

        // On ne "beaconne" pas vers DNS/NTP/HTTPS standard
        if (ctx.Settings.IgnoredRemotePorts.Contains(ci.RemotePort)) return null;

        var window = TimeSpan.FromSeconds(ctx.Settings.BeaconWindowSeconds);
        var hits = ctx.RecordBeaconHit(ci.Pid, window).OrderBy(t => t).ToArray();

        if (hits.Length < ctx.Settings.BeaconMinHits) return null;

        var cooldown = TimeSpan.FromSeconds(ctx.Settings.BeaconWindowSeconds);
        if (!ctx.TryAcquireAlertSlot($"beacon-{ci.Pid}", cooldown))
            return null;
            
        // Calcul des intervalles en secondes
        var intervals = new double[hits.Length - 1];
        for (int i = 1; i < hits.Length; i++)
            intervals[i - 1] = (hits[i] - hits[i - 1]).TotalSeconds;

        var mean = intervals.Average();
        if (mean < 5) return null; // trop rapide ? bruit, pas beacon

        var variance = intervals.Select(x => Math.Pow(x - mean, 2)).Average();
        var stdDev = Math.Sqrt(variance);
        var jitter = stdDev / mean;

        if (jitter > ctx.Settings.BeaconJitterTolerancePct) return null;

        return new DetectorResult
        {
            IsThreat = true,
            Severity = Severity.High,
            Message = $"Comportement de type beaconing détecté : PID {ci.Pid} " +
                      $"({ci.ProcessName ?? "?"}) — {hits.Length} connexions, " +
                      $"intervalle moyen {mean:F1} s (jitter {jitter:P0})",
            Details = $"Cible : {ci.RemoteIp}:{ci.RemotePort} — " +
                      $"intervalles (s) : {string.Join(", ", intervals.Select(x => x.ToString("F1")))}"
        };
    }
}