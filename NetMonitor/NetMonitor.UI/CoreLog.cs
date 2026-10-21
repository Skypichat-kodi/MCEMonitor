using System;
using System.IO;

namespace NetMonitor.UI
{
    public static class CoreLog
    {
        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "Logs"
        );

        private static readonly string LogPath = Path.Combine(LogFolder, "NetMonitor.UI.log");

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

        public static void Info(string message)  => Write(message);
        public static void Warn(string message)  => Write("[WARN] " + message);
        public static void Error(string message) => Write("[ERROR] " + message);
    }
}