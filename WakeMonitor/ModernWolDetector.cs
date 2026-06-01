using WakeMonitor;

public static class ModernWolDetector
{
    public static WolResult Detect(WakeInfo wake, string localMac, WakeMonitorSettings opt)
    {
        bool isWolAttempt = WakeEventFilter.IsWolAttempt();
        if (!isWolAttempt)
            return WolResult.Normal();

        string wolIp = WolIpDetector.GetWolSourceIp(localMac);
        string wolMac = ArpResolver.GetMacFromIp(wolIp);

        bool allowed = opt.AllowedWolMacs.Contains(wolMac);

        return allowed
            ? WolResult.WolAuthorized(wolIp, wolMac)
            : WolResult.WolSuspect(wolIp, wolMac);
    }
}

