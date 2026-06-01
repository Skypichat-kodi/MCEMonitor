using WakeMonitor;

public static class WolDetectionStrategy
{
    public static WolResult Detect(WakeInfo wake, string localMac, WakeMonitorSettings opt)
    {
        bool isModern = NetworkCardInfo.IsModernWolCapable();

        if (isModern)
            return ModernWolDetector.Detect(wake, localMac, opt);

        return LegacyWolDetector.Detect(wake);
    }
}

