using WakeMonitor;

namespace WakeMonitor
{
    public static class WakeCauseDetector
    {
        public static WolResult DetectWakeCause(WakeInfo wake, string localMac, WakeMonitorSettings opt)
        {
            // Si ce n’est pas un réveil WOL ? normal
            if (!WakeEventFilter.IsWolAttempt())
                return WolResult.Normal();

            // Détection moderne ou legacy
            return WolDetectionStrategy.Detect(wake, localMac, opt);
        }
        public static string GetProbableWakeCause(WakeInfo wake, string localMac, WakeMonitorSettings opt)
        {
            var result = DetectWakeCause(wake, localMac, opt);

            if (result == null)
                return wake.Cause ?? "Inconnue";

            if (!result.IsWol)
                return wake.Cause ?? "Réveil normal";

            return result.Tag;
        }
    }
}

