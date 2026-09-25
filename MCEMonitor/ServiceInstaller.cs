using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace MCEMonitor
{
    public static class ServiceInstaller
    {
        private const string SERVICE_TASK_NAME   = "MCEMonitor_Service";
        private const string TRAY_TASK_NAME      = "MCEMonitor_MediaMonitorTray";
        private const string ROM_TRAY_TASK_NAME  = "MCEMonitor_RomMonitorTray";
        private const string SYSTEM_TRAY_TASK_NAME = "MCEMonitor_SystemMonitorTray";

        // ============================================================
        //  Vérifier si la tâche SYSTEM du service existe
        // ============================================================
        public static bool ServiceTaskExists()
        {
            return TaskExists(SERVICE_TASK_NAME);
        }

        // ============================================================
        //  Vérifier si la tâche ONLOGON du Tray MediaMonitor existe
        // ============================================================
        public static bool TrayTaskExists()
        {
            return TaskExists(TRAY_TASK_NAME);
        }

        // ============================================================
        //  Vérifier si la tâche ONLOGON du Tray RomMonitor existe
        // ============================================================
        public static bool RomTrayTaskExists()
        {
            return TaskExists(ROM_TRAY_TASK_NAME);
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
                KryptonMessageBox.Show(LanguageManager.Get("MediaMonitor.Service.exe introuvable dans ProgramData.") ?? "MediaMonitor.Service.exe introuvable dans ProgramData.");
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
        //  Créer la tâche ONLOGON qui lance RomMonitor.Tray.exe
        // ============================================================
        public static void CreateRomTrayTask()
        {
            string trayPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "MCEMonitor",
                "RomMonitor.Tray.exe"
            );

            if (!File.Exists(trayPath))
            {
                trayPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "MCEMonitor",
                    "RomMonitor.Tray.exe"
                );
            }

            if (!File.Exists(trayPath))
            {
                // Pas la peine de créer une tâche vers un exe inexistant
                return;
            }

            string cmd =
                "schtasks /Create /TN \"" + ROM_TRAY_TASK_NAME + "\" " +
                "/SC ONLOGON " +
                $"/TR \"\\\"{trayPath}\\\"\" " +
                "/RL HIGHEST /F";

            RunAdminCommand(cmd);
        }

        // ============================================================
        //  Supprimer la tâche ONLOGON du Tray RomMonitor
        // ============================================================
        public static void DeleteRomTrayTask()
        {
            string cmd = $"schtasks /Delete /TN \"{ROM_TRAY_TASK_NAME}\" /F";
            RunAdminCommand(cmd);
        }

        // ============================================================
        //  Vérifier si la tâche ONLOGON du Tray SystemMonitor existe
        // ============================================================
        public static bool SystemTrayTaskExists()
        {
            return TaskExists(SYSTEM_TRAY_TASK_NAME);
        }

        // ============================================================
        //  Créer la tâche ONLOGON qui lance SystemMonitor.Tray.exe
        // ============================================================
        public static void CreateSystemTrayTask()
        {
            string trayPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "MCEMonitor",
                "SystemMonitor.Tray.exe"
            );

            if (!File.Exists(trayPath))
            {
                trayPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "MCEMonitor",
                    "SystemMonitor.Tray.exe"
                );
            }

            if (!File.Exists(trayPath))
                return;

            string cmd =
                "schtasks /Create /TN \"" + SYSTEM_TRAY_TASK_NAME + "\" " +
                "/SC ONLOGON " +
                $"/TR \"\\\"{trayPath}\\\"\" " +
                "/RL HIGHEST /F";

            RunAdminCommand(cmd);
        }

        // ============================================================
        //  Supprimer la tâche ONLOGON du Tray SystemMonitor
        // ============================================================
        public static void DeleteSystemTrayTask()
        {
            string cmd = $"schtasks /Delete /TN \"{SYSTEM_TRAY_TASK_NAME}\" /F";
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