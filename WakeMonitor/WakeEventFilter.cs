using System;
using System.Diagnostics.Eventing.Reader;

namespace WakeMonitor
{
    public static class WakeEventFilter
    {
        public static bool IsRealWakeEvent()
        {
            try
            {
                // On lit uniquement les derniers EventID=1 du journal System
                var query = new EventLogQuery("System", PathType.LogName,
                    "*[System[(EventID=1)]]");

                using var reader = new EventLogReader(query);

                var evt = reader.ReadEvent();
                if (evt == null)
                    return false;

                string provider = evt.ProviderName ?? "";

                // ?? Le SEUL vrai réveil Windows
                return provider.Equals("Microsoft-Windows-Power-Troubleshooter",
                                       StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}

