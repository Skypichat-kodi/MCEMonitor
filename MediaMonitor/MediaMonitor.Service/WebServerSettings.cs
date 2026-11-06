using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using MediaMonitor.Core.Services;

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

            return Path.Combine(folder, "MediaMonitor.web.config");
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

                    // Crée le fichier pour matérialiser l'état par défaut
                    settings.Save();

                    // Protège le fichier nouvellement créé
                    EnsureWebConfigProtected(path);

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

                    if (line.StartsWith("DvbViewerSwitch=", StringComparison.OrdinalIgnoreCase))
                        settings.DvbViewerSwitch = line.Split('=', 2)[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }

                // Protège le fichier au démarrage (idempotent)
                EnsureWebConfigProtected(path);
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
                sb.AppendLine("DvbViewerSwitch=" + (DvbViewerSwitch ? "true" : "false"));

                File.WriteAllText(path, sb.ToString());

                // Ré-applique l'ACL après écriture (le fichier a peut-être été recréé)
                EnsureWebConfigProtected(path);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Protège le fichier *.web.config en lecture seule pour les utilisateurs
        /// standards, tout en gardant l'écriture pour SYSTEM (le service) et
        /// Administrators (dépannage manuel).
        /// </summary>
        private static void EnsureWebConfigProtected(string path)
        {
            if (!File.Exists(path)) return;

            try
            {
                // 1) takeown
                var takeownPsi = new ProcessStartInfo("takeown", $"/f \"{path}\" /a")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(takeownPsi))
                {
                    if (p != null)
                    {
                        var takeownOut = p.StandardOutput.ReadToEndAsync();
                        var takeownErr = p.StandardError.ReadToEndAsync();

                        if (p.WaitForExit(3000))
                        {
                            _ = takeownOut.Result;
                            _ = takeownErr.Result;
                        }
                    }
                }

                // 2) icacls avec SID universels
                var icaclsPsi = new ProcessStartInfo("icacls",
                    $"\"{path}\" /inheritance:r " +
                    $"/grant \"*S-1-5-18:(F)\" " +
                    $"/grant \"*S-1-5-32-544:(F)\" " +
                    $"/grant \"*S-1-5-32-545:(R)\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p2 = Process.Start(icaclsPsi);
                if (p2 == null) return;

                var outTask = p2.StandardOutput.ReadToEndAsync();
                var errTask = p2.StandardError.ReadToEndAsync();

                if (!p2.WaitForExit(5000))
                {
                    try { p2.Kill(true); } catch { }
                    Console.WriteLine($"[PROTECT] {Path.GetFileName(path)} : timeout");
                    return;
                }

                if (p2.ExitCode == 0)
                    Console.WriteLine($"[PROTECT] {Path.GetFileName(path)} protégé");
                else
                    Console.WriteLine($"[PROTECT] Échec {Path.GetFileName(path)} : {errTask.Result}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PROTECT] Erreur : {ex.Message}");
            }
        }
    }
}