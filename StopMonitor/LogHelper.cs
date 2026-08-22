using System;
using System.IO;
using System.Text;

namespace StopMonitor
{
    public static class LogHelper
    {
        private static string _logFile = "";

        // ------------------------------------------------------------
        //  DÉFINIR LE FICHIER DE LOG
        // ------------------------------------------------------------
        public static void SetLogFile(string fileName)
        {
            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "Logs"
            );

            Directory.CreateDirectory(baseDir);

            _logFile = Path.Combine(baseDir, fileName);
        }

        // ------------------------------------------------------------
        //  EFFACER LE LOG AU DÉMARRAGE
        // ------------------------------------------------------------
        public static void Clear()
        {
            if (!string.IsNullOrEmpty(_logFile) && File.Exists(_logFile))
                File.Delete(_logFile);
        }

        // ------------------------------------------------------------
        //  ÉCRITURE SIMPLE
        // ------------------------------------------------------------
        public static void Write(string text)
        {
            if (string.IsNullOrEmpty(_logFile))
                return;

            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {text}{Environment.NewLine}";
            File.AppendAllText(_logFile, line, Encoding.UTF8);
        }

        // ------------------------------------------------------------
        //  ÉCRITURE D’UN BLOC MULTI-LIGNES
        // ------------------------------------------------------------
        public static void WriteBlock(string title, string content)
        {
            if (string.IsNullOrEmpty(_logFile))
                return;

            var sb = new StringBuilder();

            sb.AppendLine();
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {title}");
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine(content);
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine();

            File.AppendAllText(_logFile, sb.ToString(), Encoding.UTF8);
        }
    }
}
