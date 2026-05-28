using System;
using System.IO;

namespace MCEMonitor.Utils
{
    public static class AppData
    {
        public static string BasePath { get; private set; }
        public static string LogsPath => Path.Combine(BasePath, "Logs");

        public static void Initialize()
        {
            BasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor"
            );

            Directory.CreateDirectory(BasePath);
            Directory.CreateDirectory(LogsPath);
        }
    }
}

