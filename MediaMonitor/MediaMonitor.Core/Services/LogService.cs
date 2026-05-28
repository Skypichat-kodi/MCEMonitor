using System;
using System.IO;

namespace MediaMonitor.Core.Services
{
    public static class LogService
    {
        private static readonly string BaseDir =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "Logs"
            );

        static LogService()
        {
            Directory.CreateDirectory(BaseDir);
        }

        public static void WriteDebug(string text)
        {
            string path = Path.Combine(BaseDir, "debug_smb.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} - {text}{Environment.NewLine}");
        }

        public static void WriteError(string text)
        {
            string path = Path.Combine(BaseDir, "error.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {text}{Environment.NewLine}");
        }
    }
}

