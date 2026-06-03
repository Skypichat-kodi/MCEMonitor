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

        string sleepState = "Inconnue";   // <-- NOUVEAU
        string wakeCause  = "Inconnue";   // <-- NOUVEAU

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
            else if (l.StartsWith("État de veille", StringComparison.OrdinalIgnoreCase) ||
                     l.StartsWith("État du système", StringComparison.OrdinalIgnoreCase) ||
                     l.StartsWith("Sleep State", StringComparison.OrdinalIgnoreCase))
            {
                // Exemple : "État de veille : S4"
                sleepState = l[(l.IndexOf(':') + 1)..].Trim();
            }
            else if (l.StartsWith("Source de réveil", StringComparison.OrdinalIgnoreCase))
            {
                // Exemple : "Source de réveil : Power Button"
                wakeCause = l[(l.IndexOf(':') + 1)..].Trim();
            }
        }

        return new WakeInfo
        {
            WakeTime = wakeTime,
            SleepTime = sleepTime,
            SleepDuration = wakeTime - sleepTime,

            // --- NOUVEAU : séparation correcte ---
            SleepState = sleepState,
            WakeCause  = wakeCause,

            // Compatibilité avec ton code existant :
            Cause = wakeCause
        };
    }

    private static string Clean(string s)
    {
        return new string(s.Where(c => !char.IsControl(c) || c == '\n').ToArray())
            .Replace("\u200E", "")
            .Replace("\u200F", "")
            .Replace("\u202A", "")
            .Replace("\u202B", "")
            .Replace("\u202C", "")
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

