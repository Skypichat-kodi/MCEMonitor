using System;
using System.Diagnostics.Eventing.Reader;
using System.Text;

public static class KernelEventDiagnostics
{
    public static string GetInfo()
    {
        var sb = new StringBuilder();

        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[(EventID=1 or EventID=42 or EventID=107 or EventID=506 or EventID=507)]]");

            var reader = new EventLogReader(query);

            int count = 0;

            for (EventRecord evt = reader.ReadEvent(); evt != null && count < 20; evt = reader.ReadEvent())
            {
                string provider = evt.ProviderName;
                string category = evt.TaskDisplayName ?? "";
                string msg = evt.FormatDescription();

                sb.AppendLine($"Event ID : {evt.Id}");
                sb.AppendLine($"  Time : {evt.TimeCreated}");
                sb.AppendLine($"  Provider : {provider}");
                sb.AppendLine($"  Category : {category}");
                sb.AppendLine($"  Message : {msg}");

                // ?? Détection Power-Troubleshooter
                if (evt.Id == 1 && provider == "Microsoft-Windows-Power-Troubleshooter")
                {
                    sb.AppendLine("  ? Type : WAKE EVENT (Power-Troubleshooter)");
                }

                // ?? Détection Kernel-General
                if (evt.Id == 1 && provider == "Microsoft-Windows-Kernel-General")
                {
                    sb.AppendLine("  ? Type : KERNEL GENERAL (non wake)");
                }

                sb.AppendLine();
                count++;
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERREUR KernelEventDiagnostics : " + ex.Message);
        }

        return sb.ToString();
    }
}

