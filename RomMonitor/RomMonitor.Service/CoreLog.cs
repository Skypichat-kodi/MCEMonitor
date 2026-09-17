using System;
using System.IO;

namespace RomMonitor.Service
{
    /// <summary>
    /// Logger simple pour RomMonitor.Service.
    /// Écrit dans C:\ProgramData\MCEMonitor\Logs\RomMonitor.Service.log
    /// </summary>
    public static class CoreLog
    {
        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "Logs"
        );

        private static readonly string LogPath = Path.Combine(LogFolder, "RomMonitor.Service.log");

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