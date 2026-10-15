using NetMonitor.Service.Models;

namespace NetMonitor.Service.Detectors;

public interface IDetector
{
    string Name { get; }
    DetectorResult? Inspect(NetworkEvent evt, DetectorContext ctx);
}