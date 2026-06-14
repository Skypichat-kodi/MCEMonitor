using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace MCEMonitor
{
    public static class ServiceInstaller
    {
        private const string SERVICE_TASK_NAME = "MCEMonitor_Service";
        private const string TRAY_TASK_NAME = "MCEMonitor_Tray";

        // ============================================================
        //  Vérifier si la tâche SYSTEM du service existe
        // ============================================================
        public static bool ServiceTaskExists()
        {
            return TaskExists(SERVICE_TASK_NAME);
        }

        // ============================================================
        //  Vérifier si la tâche ONLOGON du Tray existe
        // ============================================================
        public static bool TrayTaskExists()
        {
            return TaskExists(TRAY_TASK_NAME);
        }

        private static bool TaskExists(string taskName)
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

        // ============================================================
        //  Créer la tâche SYSTEM qui lance MediaMonitor.Service.exe
        // ============================================================
        public static void CreateServiceTask()
        {
            string servicePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "MediaMonitor.Service.exe"
            );

            if (!File.Exists(servicePath))
            {
                MessageBox.Show(LanguageManager.Get("ServiceInstaller_MediaMonitorServiceexe_introuvable_dans__2fb0") ?? "MediaMonitor.Service.exe introuvable dans ProgramData.");
                return;
            }

            string cmd =
                "schtasks /Create /TN \"" + SERVICE_TASK_NAME + "\" " +
                "/SC ONSTART " +
                $"/TR \"\\\"{servicePath}\\\"\" " +
                "/RU SYSTEM /F";

            RunAdminCommand(cmd);
        }

        // ============================================================
        //  Créer la tâche ONLOGON qui lance MediaMonitor.Tray.exe
        // ============================================================
        public static void CreateTrayTask()
        {
            string trayPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "MCEMonitor",
                "MediaMonitor.Tray.exe"
            );

            if (!File.Exists(trayPath))
            {
                trayPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "MCEMonitor",
                    "MediaMonitor.Tray.exe"
                );
            }

            string cmd =
                "schtasks /Create /TN \"" + TRAY_TASK_NAME + "\" " +
                "/SC ONLOGON " +
                $"/TR \"\\\"{trayPath}\\\"\" " +
                "/RL HIGHEST /F";

            RunAdminCommand(cmd);
        }

        // ============================================================
        //  Démarrer immédiatement la tâche SYSTEM du service
        // ============================================================
        public static void StartServiceTask()
        {
            string cmd = $"schtasks /Run /TN \"{SERVICE_TASK_NAME}\"";
            RunAdminCommand(cmd);
        }

        // ============================================================
        //  Exécuter une commande en admin (UAC)
        // ============================================================
        private static void RunAdminCommand(string cmd)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + cmd,
                Verb = "runas",
                UseShellExecute = true
            });
        }
    }
}

