using System;
using System.Diagnostics.Eventing.Reader;

public static class NetworkDiagnosticsExtensions
{
    public static bool WasMagicPacketReceived()
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=107)]]")
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            var evt = reader.ReadEvent();
            if (evt == null)
                return false;

            string msg = evt.FormatDescription()?.ToLowerInvariant() ?? "";

            return msg.Contains("network") ||
                   msg.Contains("lan") ||
                   msg.Contains("wake on lan") ||
                   msg.Contains("magic");
        }
        catch
        {
            return false;
        }
    }
}

