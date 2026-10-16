using System;
using System.IO;

namespace NetMonitor.Service
{
    public static class CoreLog
    {
        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "Logs"
        );

        private static readonly string LogPath = Path.Combine(LogFolder, "NetMonitor.Service.log");

        /// <summary>
        /// Active les logs de diagnostic détaillés ([ETW], [PortScan], etc.).
        /// À passer à true uniquement en debug (spamme le log).
        /// Peut être activé via la variable d'environnement MCEMONITOR_VERBOSE=1.
        /// </summary>
        public static bool Verbose { get; set; } =
            Environment.GetEnvironmentVariable("MCEMONITOR_VERBOSE") == "1";

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        /// <summary>Log de diagnostic : n'écrit que si Verbose=true.</summary>
        public static void Debug(string message)
        {
            if (Verbose) Write(message);
        }

        public static void Info(string message)  => Write(message);
        public static void Warn(string message)  => Write("[WARN] " + message);
        public static void Error(string message) => Write("[ERROR] " + message);

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