using System;
using System.IO;

namespace SystemMonitor.Service
{
    /// <summary>
    /// Logger simple pour SystemMonitor.Service.
    /// Écrit dans C:\ProgramData\MCEMonitor\Logs\SystemMonitor.Service.log
    /// </summary>
    public static class CoreLog
    {
        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "Logs"
        );

        private static readonly string LogPath = Path.Combine(LogFolder, "SystemMonitor.Service.log");

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);

                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Ne jamais crasher pour un log
            }
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