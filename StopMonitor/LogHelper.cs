using System;
using System.IO;

namespace StopMonitor
{
    public static class LogHelper
    {
        // Nom de fichier dynamique
        private static string _logFileName = "StopMonitor.log";

        // Permet de changer le fichier log selon le mode
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

            return Path.Combine(dir, _logFileName);
        }

        // Réinitialisation du fichier (sécurisée)
        public static void Clear()
        {
            try
            {
                string path = GetLogPath();

                // Si le fichier existe ? on le vide
                // S'il n'existe pas ? on le crée vide
                File.WriteAllText(path, string.Empty);
            }
            catch
            {
                // On ne casse jamais StopMonitor pour un log
            }
        }

        public static void Write(string message)
        {
            try
            {
                string path = GetLogPath();
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // Silence en cas d'erreur de log
            }
        }

        public static void WriteBlock(string title, string content)
        {
            try
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
            catch
            {
                // Silence en cas d'erreur de log
            }
        }
    }
}

