using System;
using System.Management;
using System.Text;

public static class UsbDiagnostics
{
    public static string GetInfo()
    {
        var sb = new StringBuilder();

        try
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_USBHub");

            foreach (var device in searcher.Get())
            {
                sb.AppendLine($"USB : {device["Name"]}");
                sb.AppendLine($"  DeviceID : {device["DeviceID"]}");
                sb.AppendLine($"  Status : {device["Status"]}");
                sb.AppendLine();
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERREUR UsbDiagnostics : " + ex.Message);
        }

        return sb.ToString();
    }
}

