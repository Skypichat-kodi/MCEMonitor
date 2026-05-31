using System;
using System.IO;

public static class WOLSuspectLogHelper
{
    private static string GetLogPath()
    {
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "Logs"
        );

        Directory.CreateDirectory(baseDir);

        return Path.Combine(baseDir, "WOL_Suspect.log");
    }

    public static void Write(string message)
    {
        try
        {
            string path = GetLogPath();
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Ne jamais casser WakeMonitor pour un log
        }
    }

    public static void WriteBlock(string title, string content)
    {
        try
        {
            string path = GetLogPath();
            string block =
                "\n------------------------------------------------------------\n" +
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {title}\n" +
                "------------------------------------------------------------\n" +
                content +
                "\n------------------------------------------------------------\n";

            File.AppendAllText(path, block);
        }
        catch
        {
            // Silence en cas d'erreur de log
        }
    }
}

