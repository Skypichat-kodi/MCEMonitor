using System;
using System.IO;

namespace SystemMonitor.Service
{
    /// <summary>
    /// Configuration email partagée avec MediaMonitor.
    /// Lit C:\ProgramData\MCEMonitor\email.config
    /// </summary>
    public class EmailConfig
    {
        public string Server { get; set; } = "";
        public int Port { get; set; } = 465;
        public string From { get; set; } = "";
        public string Password { get; set; } = "";
        public string To { get; set; } = "";
        public string SecurityMode { get; set; } = "SSL";

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "email.config"
        );

        public static EmailConfig Load()
        {
            var cfg = new EmailConfig();

            try
            {
                if (!File.Exists(ConfigPath))
                    return cfg;

                foreach (var line in File.ReadAllLines(ConfigPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains('='))
                        continue;

                    var parts = line.Split('=', 2);
                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    switch (key)
                    {
                        case "Server":       cfg.Server = value; break;
                        case "Port":         if (int.TryParse(value, out int p)) cfg.Port = p; break;
                        case "From":         cfg.From = value; break;
                        case "Password":     cfg.Password = value; break;
                        case "To":           cfg.To = value; break;
                        case "SecurityMode": cfg.SecurityMode = value; break;
                    }
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("Erreur Load EmailConfig : " + ex.Message);
            }

            return cfg;
        }
    }
}