using System;
using System.Diagnostics.Eventing.Reader;

public static class WakeCauseDetector
{
    public static string GetProbableWakeCause()
    {
        // 1) Vérifier si un périphérique USB s’est reconnecté juste après le réveil
        string usb = UsbDeviceDetector.GetLastUsbDevice();
        if (!string.IsNullOrWhiteSpace(usb))
            return "USB (" + usb + ")";

        // 2) Vérifier Kernel-Power 107 (sortie de veille)
        string kernelCause = GetKernelPowerCause();
        if (kernelCause != null)
            return kernelCause;

        // 3) Vérifier les Wake Timers Windows
        if (WakeTimerDetector.HasActiveWakeTimer())
            return "Wake Timer (Windows Update)";

        // 4) Vérifier si un paquet WoL a été reçu
        if (NetworkDiagnosticsExtensions.WasMagicPacketReceived())
            return "Wake-on-LAN";


        // 5) Vérifier les événements ACPI (GPE / PME / RTC)
        string acpi = GetAcpiWakeReason();
        if (acpi != null)
            return acpi;

        // 6) Si rien trouvé ? bouton power probable
        return "Bouton d’alimentation (probable)";
    }

    private static string GetKernelPowerCause()
    {
        var query = new EventLogQuery("System", PathType.LogName,
            "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=107)]]")
        {
            ReverseDirection = true
        };

        using var reader = new EventLogReader(query);
        var evt = reader.ReadEvent();
        if (evt == null)
            return null;

        string msg = evt.FormatDescription()?.ToLowerInvariant() ?? "";

        if (msg.Contains("usb"))
            return "USB";
        if (msg.Contains("network") || msg.Contains("lan"))
            return "Wake-on-LAN";
        if (msg.Contains("power button") || msg.Contains("bouton"))
            return "Bouton d’alimentation";
        if (msg.Contains("timer"))
            return "Wake Timer";

        return null;
    }

    private static string GetAcpiWakeReason()
    {
        var query = new EventLogQuery("System", PathType.LogName,
            "*[System[Provider[@Name='Microsoft-Windows-Kernel-Boot']]]")
        {
            ReverseDirection = true
        };

        using var reader = new EventLogReader(query);
        var evt = reader.ReadEvent();
        if (evt == null)
            return null;

        string msg = evt.FormatDescription()?.ToLowerInvariant() ?? "";

        if (msg.Contains("gpe"))
            return "ACPI (GPE)";
        if (msg.Contains("pme"))
            return "ACPI (PME)";
        if (msg.Contains("rtc"))
            return "Réveil RTC (horloge interne)";

        return null;
    }
}

