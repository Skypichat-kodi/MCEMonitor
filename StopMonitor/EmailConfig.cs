using System;
using System.IO;

namespace StopMonitor
{
    public class EmailConfig
    {
        public string From { get; set; } = "";
        public string Password { get; set; } = "";
        public string To { get; set; } = "";
        public string Server { get; set; } = "";
        public int Port { get; set; } = 465;
        public string SecurityMode { get; set; } = "SSL";

        // ------------------------------------------------------------
        //  CHEMIN DU FICHIER DE CONFIG
        // ------------------------------------------------------------
        private static string GetConfigPath()
        {
            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor"
            );

            Directory.CreateDirectory(baseDir);

            return Path.Combine(baseDir, "email.config");
        }

        // ------------------------------------------------------------
        //  CHARGEMENT / SAUVEGARDE
        // ------------------------------------------------------------
        public static EmailConfig Load() => LoadFromUserFolder();
        public void Save() => SaveToUserFolder();

        public static EmailConfig LoadFromUserFolder()
        {
            string path = GetConfigPath();
            var cfg = new EmailConfig();

            if (!File.Exists(path))
                return cfg;

            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split('=', 2);
                if (parts.Length != 2)
                    continue;

                string key = parts[0].Trim().ToLowerInvariant();
                string value = parts[1].Trim();

                switch (key)
                {
                    case "from": cfg.From = value; break;
                    case "password": cfg.Password = value; break;
                    case "to": cfg.To = value; break;
                    case "server": cfg.Server = value; break;
                    case "port": if (int.TryParse(value, out int p)) cfg.Port = p; break;
                    case "securitymode": cfg.SecurityMode = value; break;
                }
            }

            return cfg;
        }

        public void SaveToUserFolder()
        {
            string path = GetConfigPath();

            using var sw = new StreamWriter(path, false);

            sw.WriteLine("From=" + From);
            sw.WriteLine("Password=" + Password);
            sw.WriteLine("To=" + To);
            sw.WriteLine("Server=" + Server);
            sw.WriteLine("Port=" + Port);
            sw.WriteLine("SecurityMode=" + SecurityMode);
        }
    }
}
