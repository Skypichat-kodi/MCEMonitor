using System;
using System.Diagnostics.Eventing.Reader;

public static class WakeEventReader
{
    public static WakeInfo GetLastWakeInfo()
    {
        var query = new EventLogQuery("System", PathType.LogName,
            "*[System/EventID=1]")
        {
            ReverseDirection = true // ?? LIRE LE PLUS RÉCENT
        };

        using var reader = new EventLogReader(query);
        var evt = reader.ReadEvent();

        if (evt == null)
            return new WakeInfo { WakeTime = DateTime.Now };

        DateTime wakeTime = evt.TimeCreated ?? DateTime.Now;

        string cause = "Inconnue";

        try
        {
            string desc = evt.FormatDescription();

            if (!string.IsNullOrWhiteSpace(desc))
            {
                if (desc.Contains(":"))
                    cause = desc[(desc.IndexOf(':') + 1)..].Trim();
                else
                    cause = desc.Trim();
            }

            if (string.IsNullOrWhiteSpace(cause))
                cause = "Inconnue";
        }
        catch
        {
            cause = "Inconnue";
        }

        cause = NormalizeCause(cause);

        DateTime sleepTime = DateTime.Now;

        var sleepQuery = new EventLogQuery("System", PathType.LogName,
            "*[System/EventID=42]")
        {
            ReverseDirection = true // ?? LIRE LE PLUS RÉCENT
        };

        using var sleepReader = new EventLogReader(sleepQuery);
        var sleepEvt = sleepReader.ReadEvent();

        if (sleepEvt != null)
            sleepTime = sleepEvt.TimeCreated ?? DateTime.Now;

        return new WakeInfo
        {
            WakeTime = wakeTime,
            SleepTime = sleepTime,
            SleepDuration = wakeTime - sleepTime,
            Cause = cause
        };
    }

    private static string NormalizeCause(string raw)
    {
        string r = raw.ToLowerInvariant();

        if (r.Contains("usb"))
            return "USB (" + UsbDeviceDetector.GetLastUsbDevice() + ")";

        if (r.Contains("keyboard") || r.Contains("clavier"))
            return "Clavier";

        if (r.Contains("mouse") || r.Contains("souris"))
            return "Souris";

        if (r.Contains("network") || r.Contains("réseau") || r.Contains("lan"))
            return "Wake-on-LAN";

        if (r.Contains("timer"))
            return "Timer";

        if (r.Contains("power button") || r.Contains("bouton"))
            return "Bouton d’alimentation";

        if (r.Contains("bluetooth"))
            return "Bluetooth";

        if (r.Contains("infrared") || r.Contains("ir"))
            return "Infrarouge";

        return raw.Trim();
    }
}

