using System;
using System.Linq;
using System.Management;

public static class UsbDeviceDetector
{
    public static string GetLastUsbDevice()
    {
        try
        {
            var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'USB'");

            var devices = searcher.Get()
                .Cast<ManagementObject>()
                .Select(d => d["Name"]?.ToString())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            if (devices.Count == 0)
                return "USB";

            return devices.Last();
        }
        catch
        {
            return "USB";
        }
    }
}

