using System;
using System.IO;

namespace MCEMonitor.Utils
{
    public static class AppData
    {
        private static string? _basePath;

        public static string BasePath =>
            _basePath ?? throw new InvalidOperationException(
                "AppData.Initialize() doit être appelée avant tout accès à BasePath.");

        public static string LogsPath => Path.Combine(BasePath, "Logs");

        public static void Initialize()
        {
            _basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor"
            );

            Directory.CreateDirectory(_basePath);
            Directory.CreateDirectory(LogsPath);
        }
    }
}