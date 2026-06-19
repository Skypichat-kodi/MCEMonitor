using System;
using System.IO;
using System.Text;

namespace MediaMonitor.Service
{
    public class WebServerSettings
    {
        public bool Enabled { get; set; } = false;
        public int Port { get; set; } = 8081;

        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "admin";
        public int RetentionDays { get; set; } = 0;
        public string DvbViewerUrl { get; set; } = "";
        public string DvbViewerUser { get; set; } = "";
        public string DvbViewerPass { get; set; } = "";
        public bool DvbViewerSwitch { get; set; } = false;

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
                    settings.Enabled = false;
                    settings.Port = 8081;
                    settings.Username = "admin";
                    settings.Password = "admin";
                    return settings;
                }

                var lines = File.ReadAllLines(path);

                foreach (var line in lines)
                {
                    if (line.StartsWith("Enabled=", StringComparison.OrdinalIgnoreCase))
                        settings.Enabled = line.Split('=')[1].Trim().ToLower() == "true";

                    if (line.StartsWith("Port=", StringComparison.OrdinalIgnoreCase))
                        if (int.TryParse(line.Split('=')[1].Trim(), out int p))
                            settings.Port = p;

                    if (line.StartsWith("Username=", StringComparison.OrdinalIgnoreCase))
                        settings.Username = line.Split('=', 2)[1].Trim();

                    if (line.StartsWith("Password=", StringComparison.OrdinalIgnoreCase))
                        settings.Password = line.Split('=', 2)[1].Trim();

                    if (line.StartsWith("RetentionDays=", StringComparison.OrdinalIgnoreCase))
                        if (int.TryParse(line.Split('=', 2)[1].Trim(), out int r))
                            settings.RetentionDays = r;

                    if (line.StartsWith("DvbViewerUrl=", StringComparison.OrdinalIgnoreCase))
                        settings.DvbViewerUrl = line.Split('=', 2)[1].Trim();

                    if (line.StartsWith("DvbViewerUser=", StringComparison.OrdinalIgnoreCase))
                        settings.DvbViewerUser = line.Split('=', 2)[1].Trim();

                    if (line.StartsWith("DvbViewerPass=", StringComparison.OrdinalIgnoreCase))
                        settings.DvbViewerPass = line.Split('=', 2)[1].Trim();

                    // ?? AJOUT MANQUANT
                    if (line.StartsWith("DvbViewerSwitch=", StringComparison.OrdinalIgnoreCase))
                        settings.DvbViewerSwitch = line.Split('=', 2)[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                string path = GetConfigPath();

                var sb = new StringBuilder();
                sb.AppendLine("Enabled=" + (Enabled ? "true" : "false"));
                sb.AppendLine("Port=" + Port);
                sb.AppendLine("Username=" + Username);
                sb.AppendLine("Password=" + Password);
                sb.AppendLine("RetentionDays=" + RetentionDays);
                sb.AppendLine("DvbViewerUrl=" + DvbViewerUrl);
                sb.AppendLine("DvbViewerUser=" + DvbViewerUser);
                sb.AppendLine("DvbViewerPass=" + DvbViewerPass);

                // ?? AJOUT MANQUANT
                sb.AppendLine("DvbViewerSwitch=" + (DvbViewerSwitch ? "true" : "false"));

                File.WriteAllText(path, sb.ToString());
            }
            catch
            {
            }
        }
    }
}

