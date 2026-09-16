using System;
using System.IO;

namespace MediaMonitor.Service.Service
{
    /// <summary>
    /// Écrit dans MediaMonitor.Schedule.log (fichier de planification).
    /// </summary>
    public static class ScheduleLogger
    {
        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "Logs"
        );

        private static readonly string ScheduleLogPath = Path.Combine(LogFolder, "MediaMonitor.Schedule.log");

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                File.AppendAllText(ScheduleLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n");
            }
            catch { }
        }

        public static void Clear()
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                File.WriteAllText(ScheduleLogPath, string.Empty);
            }
            catch { }
        }
    }
}