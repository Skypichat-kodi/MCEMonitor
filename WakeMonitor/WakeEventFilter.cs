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
                // On lit UNIQUEMENT les vrais réveils Power-Troubleshooter
                var query = new EventLogQuery(
                    "System",
                    PathType.LogName,
                    "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and (EventID=1)]]"
                );

                using var reader = new EventLogReader(query);

                var evt = reader.ReadEvent();
                if (evt == null)
                    return false;

                // Ici, plus besoin de vérifier le provider : il est déjà filtré
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

