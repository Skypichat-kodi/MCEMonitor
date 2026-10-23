using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

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

        private const int QUERY_TIMEOUT_MS = 5000;   // schtasks /Query
        private const int ADMIN_TIMEOUT_MS = 30000;  // schtasks /Create|Delete avec UAC

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
        //  CRÉATION — TRAY
        // ============================================================
        public static string CreateClientTrayTask()
        {
            string exePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "MCEMonitor",
                "MCEMonitorClient.Tray.exe");

            if (!File.Exists(exePath))
            {
                // Fallback x86
                exePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "MCEMonitor",
                    "MCEMonitorClient.Tray.exe");
            }

            if (!File.Exists(exePath))
                return "ERREUR : MCEMonitorClient.Tray.exe introuvable.";

            // ONLOGON : au démarrage de session (utilisateur normal, pas SYSTEM)
            return RunAdmin(
                "schtasks /Create /TN \"MCEMonitorClient_Tray\" " +
                "/SC ONLOGON " +
                $"/TR \"\\\"{exePath}\\\"\" /RL HIGHEST /F"
            );
        }

        public static string DeleteClientTrayTask()
        {
            return RunAdmin("schtasks /Delete /TN \"MCEMonitorClient_Tray\" /F");
        }

        public static bool ClientTrayTaskExists()
        {
            return QueryTask("MCEMonitorClient_Tray");
        }

        // ============================================================
        //  OUTILS INTERNES
        // ============================================================
        private static bool QueryTask(string taskName)
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
                    return false;
                }

                string output = SafeResult(outTask);
                return output.Contains(taskName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Exécute une commande schtasks avec élévation UAC et timeout dur.
        /// - Si le processus est déjà élevé ? capture stdout/stderr normalement.
        /// - Sinon ? UAC (UseShellExecute=true, pas de capture), timeout 30 s.
        /// </summary>
        private static string RunAdmin(string args)
        {
            try
            {
                // Extraire les arguments après "schtasks" si présents
                // (on appelle schtasks.exe directement, pas cmd.exe /c :
                //  ? pas de fenêtre console parasite, pas de conhost zombie)
                string realArgs = args.StartsWith("schtasks ", StringComparison.OrdinalIgnoreCase)
                    ? args.Substring("schtasks ".Length)
                    : args;

                bool isElevated = IsProcessElevated();
                bool needUac    = !isElevated;

                var psi = new ProcessStartInfo
                {
                    FileName               = "schtasks.exe",
                    Arguments              = realArgs,
                    UseShellExecute        = needUac,          // obligatoire avec runas
                    RedirectStandardOutput = !needUac,
                    RedirectStandardError  = !needUac,
                    CreateNoWindow         = !needUac,
                    StandardOutputEncoding = needUac ? null : Encoding.GetEncoding(850),
                    StandardErrorEncoding  = needUac ? null : Encoding.GetEncoding(850)
                };

                if (needUac)
                    psi.Verb = "runas";

                using var p = Process.Start(psi);
                if (p == null)
                    return "ERREUR : impossible de lancer schtasks.";

                // --- Mode UAC : pas de capture possible ---
                if (needUac)
                {
                    if (!p.WaitForExit(ADMIN_TIMEOUT_MS))
                    {
                        try { p.Kill(true); } catch { }
                        return "ERREUR : timeout UAC/schtasks (30s).";
                    }

                    return p.ExitCode == 0
                        ? "OK"
                        : $"ERREUR : schtasks code {p.ExitCode}.";
                }

                // --- Mode capturé : lecture parallèle pour éviter le deadlock ---
                Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                Task<string> errTask = p.StandardError.ReadToEndAsync();

                if (!p.WaitForExit(ADMIN_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                    return "ERREUR : timeout schtasks (30s).";
                }

                string output = SafeResult(outTask);
                string error  = SafeResult(errTask);

                return string.IsNullOrWhiteSpace(error) ? output : error;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return "ERREUR : UAC refusé par l'utilisateur.";
            }
            catch (Exception ex)
            {
                return "ERREUR : " + ex.Message;
            }
        }

        // ============================================================
        //  Utilitaires
        // ============================================================
        private static bool IsProcessElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
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
    }
}