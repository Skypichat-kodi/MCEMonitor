using System;
using System.IO;

namespace MCEMonitorClient.Tray.Logs
{
    public static class CoreLog
    {
        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "Logs"
        );

        private static readonly string LogPath = Path.Combine(LogFolder, "MCEMonitorClient.Tray.log");

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }

        public static void Clear()
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                File.WriteAllText(LogPath, string.Empty);
            }
            catch { }
        }
    }
}