using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MCEMonitorClient.Config.Services
{
    /// <summary>
    /// Gère la tâche planifiée du Tray MCEMonitorClient.
    /// Totalement autonome : ne référence aucun autre produit MCEMonitor.
    /// </summary>
    public static class ServiceInstaller
    {
        private const string TRAY_TASK_NAME = "MCEMonitorClient_Tray";

        private const int QUERY_TIMEOUT_MS = 5000;   // schtasks /Query
        private const int ADMIN_TIMEOUT_MS = 30000;  // schtasks /Create|Delete avec UAC

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
                    FileName               = "schtasks.exe",
                    Arguments              = $"/Query /TN \"{taskName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = Encoding.GetEncoding(850),
                    StandardErrorEncoding  = Encoding.GetEncoding(850)
                };

                using var p = Process.Start(psi);
                if (p == null) return false;

                // Lecture parallèle de stdout ET stderr (évite le deadlock
                // si schtasks écrit sur stderr alors qu'on lit stdout).
                Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                Task<string> errTask = p.StandardError.ReadToEndAsync();

                // Timeout dur : jamais de blocage infini
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
        //  Exécuter une commande en admin (UAC) avec timeout dur
        // ============================================================
        private static void RunAdminCommand(string cmd)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName        = "cmd.exe",
                    Arguments       = "/c " + cmd,
                    Verb            = "runas",                      // UAC
                    UseShellExecute = true,                         // obligatoire avec runas
                    WindowStyle     = ProcessWindowStyle.Hidden
                };

                using var p = Process.Start(psi);
                if (p == null)
                {
                    Debug.WriteLine("RunAdminCommand : démarrage impossible.");
                    return;
                }

                // 30 s : le temps que l'utilisateur valide l'UAC + schtasks réponde
                if (!p.WaitForExit(ADMIN_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                    Debug.WriteLine("RunAdminCommand : timeout (UAC refusé ou schtasks bloqué).");
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // UAC refusé par l'utilisateur : pas une erreur, comportement normal
                Debug.WriteLine("RunAdminCommand : UAC refusé par l'utilisateur.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RunAdminCommand ERROR : " + ex.Message);
            }
        }

        // ============================================================
        //  Utilitaire : lecture sûre d'un Task<string>
        // ============================================================
        private static string SafeResult(Task<string> t)
        {
            try { return t.Wait(1000) ? (t.Result ?? "") : ""; }
            catch { return ""; }
        }
    }
}