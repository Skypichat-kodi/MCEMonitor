using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MCEMonitorClient.Config.Services
{
    /// <summary>
    /// Gère la tâche planifiée du service MCEMonitorClient.
    /// Utilise le dossier ProgramData\MCEMonitor partagé.
    /// </summary>
    public static class TaskSchedulerHelper
    {
        // Nom unique pour éviter toute collision avec les tâches MCEMonitor
        private const string TaskName = "MCEMonitorClient_Service";

        // Chemin du service (partagé avec MCEMonitor)
        private static readonly string ServiceExePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "MCEMonitorClient.Service.exe"
        );

        // ============================================================
        //  CRÉATION
        // ============================================================
        public static string CreateClientServiceTask()
        {
            if (!File.Exists(ServiceExePath))
                return $"ERREUR : MCEMonitorClient.Service.exe introuvable dans {Path.GetDirectoryName(ServiceExePath)}.";

            return RunAdmin(
                $"schtasks /Create /TN \"{TaskName}\" " +
                "/SC ONSTART " +
                $"/TR \"\\\"{ServiceExePath}\\\"\" /RU SYSTEM /RL HIGHEST /F"
            );
        }

        // ============================================================
        //  SUPPRESSION
        // ============================================================
        public static string DeleteClientServiceTask()
        {
            return RunAdmin($"schtasks /Delete /TN \"{TaskName}\" /F");
        }

        // ============================================================
        //  VÉRIFICATION
        // ============================================================
        public static bool ClientServiceTaskExists()
        {
            return QueryTask(TaskName);
        }

        // ============================================================
        //  OUTILS INTERNES
        // ============================================================
        private static bool QueryTask(string taskName)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{taskName}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding(850)
                });

                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                return output.Contains(taskName);
            }
            catch
            {
                return false;
            }
        }

        private static string RunAdmin(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + args,
                    Verb = "runas",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding(850),
                    StandardErrorEncoding = Encoding.GetEncoding(850)
                };

                using var p = Process.Start(psi);

                if (p == null)
                    return "ERREUR : impossible de lancer la commande.";

                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();

                return string.IsNullOrWhiteSpace(error) ? output : error;
            }
            catch (Exception ex)
            {
                return "ERREUR : " + ex.Message;
            }
        }
    }
}