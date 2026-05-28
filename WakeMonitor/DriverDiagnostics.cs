using System;
using System.Management;
using System.Text;

public static class DriverDiagnostics
{
    public static string GetInfo()
    {
        var sb = new StringBuilder();

        try
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity");

            foreach (var d in searcher.Get())
            {
                string name = d["Name"]?.ToString() ?? "";
                string status = d["Status"]?.ToString() ?? "";

                if (name.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Network", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"Driver : {name}");
                    sb.AppendLine($"  Status : {status}");
                    sb.AppendLine();
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERREUR DriverDiagnostics : " + ex.Message);
        }

        return sb.ToString();
    }
}

