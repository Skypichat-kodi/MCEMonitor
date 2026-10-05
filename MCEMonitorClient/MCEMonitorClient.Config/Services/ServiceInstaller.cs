using System;
using System.Diagnostics;
using System.IO;

namespace MCEMonitorClient.Config.Services
{
    /// <summary>
    /// Gère la tâche planifiée du Tray MCEMonitorClient.
    /// Totalement autonome : ne référence aucun autre produit MCEMonitor.
    /// </summary>
    public static class ServiceInstaller
    {
        private const string TRAY_TASK_NAME = "MCEMonitorClient_Tray";

        // ============================================================
        //  Vérifier si la tâche du Tray existe
        // ============================================================
        public static bool TrayTaskExists()
        {
            return TaskExists(TRAY_TASK_NAME);
        }

        private static bool TaskExists(string taskName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{taskName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                return output.Contains(taskName);
            }
            catch
            {
                return false;
            }
        }

        // ============================================================
        //  Créer la tâche ONLOGON qui lance MCEMonitorClient.Tray.exe
        // ============================================================
        public static void CreateTrayTask()
        {
            string trayPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "MCEMonitor",
                "MCEMonitorClient.Tray.exe"
            );

            if (!File.Exists(trayPath))
            {
                trayPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "MCEMonitor",
                    "MCEMonitorClient.Tray.exe"
                );
            }

            if (!File.Exists(trayPath))
                return;   // Pas la peine de créer une tâche vers un exe inexistant

            string cmd =
                "schtasks /Create /TN \"" + TRAY_TASK_NAME + "\" " +
                "/SC ONLOGON " +
                $"/TR \"\\\"{trayPath}\\\"\" " +
                "/RL HIGHEST /F";

            RunAdminCommand(cmd);
        }

        // ============================================================
        //  Supprimer la tâche ONLOGON du Tray
        // ============================================================
        public static void DeleteTrayTask()
        {
            string cmd = $"schtasks /Delete /TN \"{TRAY_TASK_NAME}\" /F";
            RunAdminCommand(cmd);
        }

        // ============================================================
        //  Exécuter une commande en admin (UAC)
        // ============================================================
        private static void RunAdminCommand(string cmd)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + cmd,
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RunAdminCommand ERROR : " + ex.Message);
            }
        }
    }
}