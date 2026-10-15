using System;
using System.IO;

namespace NetMonitor.Tray.Logs
{
    public static class CoreLog
    {
        private static readonly object _lock = new();
        private static string LogDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MCEMonitor", "NetMonitor", "TrayLogs");

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                var file = Path.Combine(LogDir, $"tray-{DateTime.UtcNow:yyyyMMdd}.log");
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                lock (_lock) File.AppendAllText(file, line);
            }
            catch { }
        }

        public static void Info(string message)  => Write(message);
        public static void Warn(string message)  => Write("[WARN] " + message);
        public static void Error(string message) => Write("[ERROR] " + message);
    }
}