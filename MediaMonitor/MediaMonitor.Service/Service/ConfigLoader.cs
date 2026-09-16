using System;
using System.IO;

namespace MediaMonitor.Service.Service
{
    /// <summary>
    /// Charge les configurations (Shutdown.config, MediaMonitor.Service.config).
    /// </summary>
    public static class ConfigLoader
    {
        private static readonly string ConfigFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor"
        );

        /// <summary>
        /// Lit Shutdown.config et renvoie (hour, minute), ou null si absent.
        /// </summary>
        public static (int hour, int minute)? LoadShutdownTime()
        {
            try
            {
                string path = Path.Combine(ConfigFolder, "Shutdown.config");
                if (!File.Exists(path))
                    return null;

                var lines = File.ReadAllLines(path);

                int hour = -1;
                int minute = -1;

                foreach (var line in lines)
                {
                    if (line.StartsWith("Hour="))
                        hour = int.Parse(line.Split('=')[1]);

                    if (line.StartsWith("Minute="))
                        minute = int.Parse(line.Split('=')[1]);
                }

                return (hour >= 0 && minute >= 0) ? (hour, minute) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Charge l'état d'activation de l'envoi d'email.
        /// </summary>
        public static bool LoadEmailEnabled()
        {
            try
            {
                string path = Path.Combine(ConfigFolder, "MediaMonitor.Service.config");

                if (!File.Exists(path))
                    return true;

                string content = File.ReadAllText(path).Trim().ToLower();
                return content.Contains("true");
            }
            catch
            {
                return true;
            }
        }
    }
}