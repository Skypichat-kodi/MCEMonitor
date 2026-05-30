using System;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;

public static class WakeEventReader
{
    public static WakeInfo GetLastWakeInfo()
    {
        var query = new EventLogQuery(
            "System",
            PathType.LogName,
            "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and (EventID=1)]]"
        )
        {
            ReverseDirection = true
        };

        using var reader = new EventLogReader(query);
        var evt = reader.ReadEvent();

        if (evt == null)
            return new WakeInfo { WakeTime = DateTime.Now };

        string desc = Clean(evt.FormatDescription() ?? "");
        string[] lines = desc.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        DateTime wakeTime = evt.TimeCreated ?? DateTime.Now;
        DateTime sleepTime = wakeTime;
        string source = "Inconnue";

        foreach (var raw in lines)
        {
            string l = Clean(raw);

            if (l.StartsWith("Temps de veille", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseDate(l, out var dt))
                    sleepTime = dt.ToLocalTime();
            }
            else if (l.StartsWith("Temps de réveil", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseDate(l, out var dt))
                    wakeTime = dt.ToLocalTime();
            }
            else if (l.StartsWith("Source de réveil", StringComparison.OrdinalIgnoreCase))
            {
                source = l[(l.IndexOf(':') + 1)..].Trim();
            }
        }

        return new WakeInfo
        {
            WakeTime = wakeTime,
            SleepTime = sleepTime,
            SleepDuration = wakeTime - sleepTime,
            Cause = source
        };
    }

    private static string Clean(string s)
    {
        // Supprime les caractères invisibles (RTL, LTR, etc.)
        return new string(s.Where(c => !char.IsControl(c) || c == '\n').ToArray())
            .Replace("\u200E", "") // LRM
            .Replace("\u200F", "") // RLM
            .Replace("\u202A", "") // LRE
            .Replace("\u202B", "") // RLE
            .Replace("\u202C", "") // PDF
            .Trim();
    }

    private static bool TryParseDate(string line, out DateTime dt)
    {
        dt = DateTime.MinValue;

        int idx = line.IndexOf(':');
        if (idx < 0)
            return false;

        string raw = Clean(line[(idx + 1)..].Trim());

        return DateTime.TryParse(raw, out dt);
    }
}

