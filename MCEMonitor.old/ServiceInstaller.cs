using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace MCEMonitor
{
    public static class ServiceInstaller
    {
        private const string SERVICE_TASK_NAME      = "MCEMonitor_Service";
        private const string TRAY_TASK_NAME         = "MCEMonitor_MediaMonitorTray";
        private const string ROM_TRAY_TASK_NAME     = "MCEMonitor_RomMonitorTray";
        private const string SYSTEM_TRAY_TASK_NAME  = "MCEMonitor_SystemMonitorTray";
        private const string NET_TRAY_TASK_NAME     = "MCEMonitor_NetMonitorTray";

        private const int QUERY_TIMEOUT_MS = 5000;   // schtasks /Query
        private const int ADMIN_TIMEOUT_MS = 30000;  // schtasks /Create|Delete|Run avec UAC

        // ============================================================
        //  Vérifier si la tâche SYSTEM du service existe
        // ============================================================
        public static bool ServiceTaskExists()      => TaskExists(SERVICE_TASK_NAME);
        public static bool TrayTaskExists()         => TaskExists(TRAY_TASK_NAME);
        public static bool RomTrayTaskExists()      => TaskExists(ROM_TRAY_TASK_NAME);
        public static bool SystemTrayTaskExists()   => TaskExists(SYSTEM_TRAY_TASK_NAME);
        public static bool NetTrayTaskExists()      => TaskExists(NET_TRAY_TASK_NAME);

        private static bool TaskExists(string taskName)
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "schtasks.exe",
                Arguments              = $"/Query /TN \"{taskName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.GetEncoding(850),
                StandardErrorEncoding  = Encoding.GetEncoding(850)
            };

            try
            {
                using var p = Process.Start(psi);
                if (p == null) return false;

                // Lecture parallèle + timeout dur (évite deadlock et blocage infini)
                Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                Task<string> errTask = p.StandardError.ReadToEndAsync();

                if (!p.WaitForExit(QUERY_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                    return false;   // tâche considérée absente si schtasks ne répond pas
                }

                string output = SafeResult(outTask);
                return output.Contains(taskName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string SafeResult(Task<string> t)
        {
            try { return t.Wait(1000) ? (t.Result ?? "") : ""; }
            catch { return ""; }
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
                KryptonMessageBox.Show(
                    LanguageManager.Get("MediaMonitor.Service.exe introuvable dans ProgramData.")
                    ?? "MediaMonitor.Service.exe introuvable dans ProgramData.");
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
                return;   // pas la peine de créer une tâche vers un exe inexistant

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
            RunAdminCommand($"schtasks /Delete /TN \"{ROM_TRAY_TASK_NAME}\" /F");
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

        public static void DeleteSystemTrayTask()
        {
            RunAdminCommand($"schtasks /Delete /TN \"{SYSTEM_TRAY_TASK_NAME}\" /F");
        }

        // ============================================================
        //  Créer la tâche ONLOGON qui lance NetMonitor.Tray.exe
        // ============================================================
        public static void CreateNetTrayTask()
        {
            string trayPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "MCEMonitor",
                "NetMonitor.Tray.exe"
            );

            if (!File.Exists(trayPath))
            {
                trayPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "MCEMonitor",
                    "NetMonitor.Tray.exe"
                );
            }

            if (!File.Exists(trayPath))
                return;

            string cmd =
                "schtasks /Create /TN \"" + NET_TRAY_TASK_NAME + "\" " +
                "/SC ONLOGON " +
                $"/TR \"\\\"{trayPath}\\\"\" " +
                "/RL HIGHEST /F";

            RunAdminCommand(cmd);
        }

        public static void DeleteNetTrayTask()
        {
            RunAdminCommand($"schtasks /Delete /TN \"{NET_TRAY_TASK_NAME}\" /F");
        }

        // ============================================================
        //  Démarrer immédiatement la tâche SYSTEM du service
        // ============================================================
        public static void StartServiceTask()
        {
            RunAdminCommand($"schtasks /Run /TN \"{SERVICE_TASK_NAME}\"");
        }

        // ============================================================
        //  Exécuter une commande schtasks en admin (UAC) avec timeout
        // ============================================================
        private static void RunAdminCommand(string cmd)
        {
            // On passe directement par schtasks.exe (pas de cmd.exe)
            // ? pas de fenêtre console inutile, pas de conhost parasite.
            const string exe = "schtasks.exe";

            // Extraire les arguments après "schtasks"
            string args = cmd.StartsWith("schtasks ", StringComparison.OrdinalIgnoreCase)
                ? cmd.Substring("schtasks ".Length)
                : cmd;

            var psi = new ProcessStartInfo
            {
                FileName        = exe,
                Arguments       = args,
                Verb            = "runas",       // déclenche l'UAC
                UseShellExecute = true           // ?? OBLIGATOIRE avec runas
            };

            try
            {
                using var p = Process.Start(psi);
                if (p == null) return;

                // Timeout dur : 30 s (le temps que l'utilisateur valide l'UAC + schtasks réponde)
                if (!p.WaitForExit(ADMIN_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // UAC refusé par l'utilisateur — silencieux ou log si besoin
            }
            catch
            {
                // Autre erreur — silencieux ou log si besoin
            }
        }
    }
}