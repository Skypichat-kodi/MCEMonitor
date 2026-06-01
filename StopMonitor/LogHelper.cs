using System;
using System.IO;

namespace StopMonitor
{
    public static class LogHelper
    {
        // Nouveau : nom de fichier dynamique
        private static string _logFileName = "StopMonitor.log";

        // Nouveau : permet de changer le fichier log selon le mode
        public static void SetLogFile(string fileName)
        {
            _logFileName = fileName;
        }

        private static string GetLogPath()
        {
            // ProgramData + dossier Logs
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(programData, "MCEMonitor", "Logs");

            Directory.CreateDirectory(dir);

            // Utilise le nom dynamique
            return Path.Combine(dir, _logFileName);
        }

        // Réinitialisation du fichier
        public static void Clear()
        {
            string path = GetLogPath();
            File.WriteAllText(path, string.Empty);
        }

        public static void Write(string message)
        {
            string path = GetLogPath();
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
            File.AppendAllText(path, line + Environment.NewLine);
        }

        public static void WriteBlock(string title, string content)
        {
            string path = GetLogPath();
            string block =
                "\n------------------------------------------------------------\n" +
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {title}\n" +
                "------------------------------------------------------------\n" +
                content +
                "\n------------------------------------------------------------\n";

            File.AppendAllText(path, block);
        }
    }
}

