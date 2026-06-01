using WakeMonitor;

namespace WakeMonitor
{
    public static class LegacyWolDetector
    {
        public static WolResult Detect(WakeInfo wake)
        {
            return WolResult.WolLegacy();
        }
    }
}

