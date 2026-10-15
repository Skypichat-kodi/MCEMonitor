using NetMonitor.Service.Models;

namespace NetMonitor.Service.Detectors;

/// <summary>
/// Détecteur qui réagit à l'ouverture d'une connexion (TCP connect ou UDP send),
/// tel que fourni par EtwConnectionWatcher.
/// </summary>
public interface IConnectionAwareDetector
{
    string Name { get; }
    DetectorResult? InspectConnection(ConnectionInfo ci, DetectorContext ctx);
}