using System;
using System.IO;

namespace WakeMonitor
{
    public static class WOLLogHelper
    {
        private static readonly string LogPath;

        static WOLLogHelper()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            // ?? Dossier Logs (comme tes autres modules)
            string dir = Path.Combine(programData, "MCEMonitor", "Logs");
            Directory.CreateDirectory(dir);

            LogPath = Path.Combine(dir, "WOL.log");
        }

        public static void Write(string message)
        {
            try
            {
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        public static void WriteBlock(string title, string content)
        {
            try
            {
                File.AppendAllText(LogPath,
                    $"\n===== {title} ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) =====\n{content}\n");
            }
            catch { }
        }
    }
}

