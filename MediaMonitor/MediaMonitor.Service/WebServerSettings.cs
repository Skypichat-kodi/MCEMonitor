using System;
using System.IO;

namespace MediaMonitor.Service
{
    public class WebServerSettings
    {
        public bool Enabled { get; set; } = false;
        public int Port { get; set; } = 8081;

        private static string GetConfigPath()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor"
            );

            Directory.CreateDirectory(folder);

            return Path.Combine(folder, "MediaMonitor.Web.config");
        }

        public static WebServerSettings Load()
        {
            var settings = new WebServerSettings();
            string path = GetConfigPath();

            try
            {
                if (!File.Exists(path))
                {
                    // Valeurs par défaut
                    settings.Enabled = false;
                    settings.Port = 8081;
                    return settings;
                }

                var lines = File.ReadAllLines(path);

                foreach (var line in lines)
                {
                    if (line.StartsWith("Enabled=", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.Enabled = line.Split('=')[1].Trim().ToLower() == "true";
                    }

                    if (line.StartsWith("Port=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(line.Split('=')[1].Trim(), out int p))
                            settings.Port = p;
                    }
                }
            }
            catch
            {
                // En cas d’erreur, on garde les valeurs par défaut
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                string path = GetConfigPath();

                string content =
                    "Enabled=" + (Enabled ? "true" : "false") + Environment.NewLine +
                    "Port=" + Port;

                File.WriteAllText(path, content);
            }
            catch
            {
                // On ignore les erreurs pour éviter de planter le service
            }
        }
    }
}

