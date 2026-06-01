using System;

namespace WakeMonitor
{
    public static class WolIpDetector
    {
        public static string GetWolSourceIp(string localMac)
        {
            try
            {
                string ip = ArpResolver.GetMostRecentIp();
                if (!string.IsNullOrWhiteSpace(ip))
                    return ip;
            }
            catch { }

            return "0.0.0.0"; // fallback
        }
    }
}

