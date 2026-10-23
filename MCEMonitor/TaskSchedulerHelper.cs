using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MCEMonitor.Utils
{
    public static class TaskSchedulerHelper
    {
        private const int QUERY_TIMEOUT_MS = 5000;   // schtasks /Query
        private const int ADMIN_TIMEOUT_MS = 30000;  // schtasks /Create|Delete|Run avec UAC

        // ============================================================
        // WAKE MONITOR
        // ============================================================
        public static string CreateWakeTask()
        {
            string exe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "WakeMonitor.exe"
            );

            string query =
                "\"*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]\"";

            return RunAdmin(
                "schtasks /Create /TN \"MCEMonitor_WakeMonitor\" " +
                "/SC ONEVENT /EC System /MO " + query + " " +
                $"/TR \"\\\"{exe}\\\"\" /RU SYSTEM /RL HIGHEST /F"
            );
        }

        public static string DeleteWakeTask()
            => RunAdmin("schtasks /Delete /TN \"MCEMonitor_WakeMonitor\" /F");

        public static bool WakeTaskExists()
            => QueryTask("MCEMonitor_WakeMonitor");

        // ============================================================
        // MEDIA MONITOR – SERVICE (DÉMARRAGE AUTOMATIQUE)
        // ============================================================
        public static string CreateMediaMonitorServiceTask()
        {
            string exePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "MediaMonitor.Service.exe"
            );

            if (!File.Exists(exePath))
                return "ERREUR : MediaMonitor.Service.exe introuvable.";

            return RunAdmin(
                "schtasks /Create /TN \"MCEMonitor_MediaMonitorService\" " +
                "/SC ONSTART " +
                $"/TR \"\\\"{exePath}\\\"\" /RU SYSTEM /RL HIGHEST /F"
            );
        }

        public static string DeleteMediaMonitorServiceTask()
            => RunAdmin("schtasks /Delete /TN \"MCEMonitor_MediaMonitorService\" /F");

        public static bool MediaMonitorServiceTaskExists()
            => QueryTask("MCEMonitor_MediaMonitorService");

        // ============================================================
        // SHUTDOWN (ON / OFF)
        // ============================================================
        public static string CreateShutdownTask(int hour, int minute, string mode)
        {
            string exePath = Application.ExecutablePath;

            int taskHour   = hour;
            int taskMinute = minute - 5;   // 5 min avant l'heure réelle

            if (taskMinute < 0)
            {
                taskMinute += 60;
                taskHour   -= 1;
                if (taskHour < 0) taskHour += 24;
            }

            return RunAdmin(
                "schtasks /Create /TN \"MCEMonitor_Shutdown\" " +
                "/SC DAILY /ST " + $"{taskHour:D2}:{taskMinute:D2} " +
                $"/TR \"\\\"{exePath}\\\" --run-scheduled {mode}\" " +
                "/RU SYSTEM /RL HIGHEST /F"
            );
        }

        public static string DeleteShutdownTask()
            => RunAdmin("schtasks /Delete /TN \"MCEMonitor_Shutdown\" /F");

        public static bool ShutdownTaskExists()
            => QueryTask("MCEMonitor_Shutdown");

        public static (int hour, int minute)? GetShutdownTaskTime()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "schtasks.exe",
                    Arguments              = "/Query /TN \"MCEMonitor_Shutdown\" /V /FO LIST",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = Encoding.GetEncoding(850),
                    StandardErrorEncoding  = Encoding.GetEncoding(850)
                };

                using var p = Process.Start(psi);
                if (p == null) return null;

                Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                Task<string> errTask = p.StandardError.ReadToEndAsync();

                if (!p.WaitForExit(QUERY_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                    return null;
                }

                string output = SafeResult(outTask);

                foreach (var line in output.Split('\n'))
                {
                    // Attention : selon la locale, "Heure de début" peut être mal encodé
                    // (dtbut dans le fichier source d'origine). On reste tolérant.
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("Heure de d", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = trimmed.Split(':');
                        if (parts.Length >= 2)
                        {
                            string time = parts[^1].Trim();
                            string[] hhmm = time.Split(':');
                            if (hhmm.Length >= 2 &&
                                int.TryParse(hhmm[0], out int h) &&
                                int.TryParse(hhmm[1], out int m))
                            {
                                return (h, m);
                            }
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public static string GetShutdownTaskMode()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "schtasks.exe",
                    Arguments              = "/Query /TN \"MCEMonitor_Shutdown\" /V /FO LIST",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = Encoding.GetEncoding(850),
                    StandardErrorEncoding  = Encoding.GetEncoding(850)
                };

                using var p = Process.Start(psi);
                if (p == null) return null;

                Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                Task<string> errTask = p.StandardError.ReadToEndAsync();

                if (!p.WaitForExit(QUERY_TIMEOUT_MS))
                {
                    try { p.Kill(true); } catch { }
                    return null;
                }

                string output = SafeResult(outTask).ToLowerInvariant();

                if (output.Contains("--run-scheduled sleep"))    return "sleep";
                if (output.Contains("--run-scheduled shutdown")) return "shutdown";

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ============================================================
        // ROM MONITOR
        // ============================================================
        public static string CreateRomMonitorTask()
        {
            string exePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "RomMonitor.Service.exe"
            );

            if (!File.Exists(exePath))
                return "ERREUR : RomMonitor.Service.exe introuvable.";

            return RunAdmin(
                "schtasks /Create /TN \"MCEMonitor_RomMonitorService\" " +
                "/SC ONSTART " +
                $"/TR \"\\\"{exePath}\\\"\" /RU SYSTEM /RL HIGHEST /F"
            );
        }

        public static string DeleteRomMonitorTask()
            => RunAdmin("schtasks /Delete /TN \"MCEMonitor_RomMonitorService\" /F");

        public static bool RomMonitorTaskExists()
            => QueryTask("MCEMonitor_RomMonitorService");

        // ============================================================
        // SYSTEM MONITOR
        // ============================================================
        public static string CreateSystemMonitorTask()
        {
            string exePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "SystemMonitor.Service.exe"
            );

            if (!File.Exists(exePath))
                return "ERREUR : SystemMonitor.Service.exe introuvable.";

            return RunAdmin(
                "schtasks /Create /TN \"MCEMonitor_SystemMonitorService\" " +
                "/SC ONSTART " +
                $"/TR \"\\\"{exePath}\\\"\" /RU SYSTEM /RL HIGHEST /F"
            );
        }

        public static string DeleteSystemMonitorTask()
            => RunAdmin("schtasks /Delete /TN \"MCEMonitor_SystemMonitorService\" /F");

        public static bool SystemMonitorTaskExists()
            => QueryTask("MCEMonitor_SystemMonitorService");

        // ============================================================
        // NET MONITOR
        // ============================================================
        public static string CreateNetMonitorTask()
        {
            string exePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "NetMonitor.Service.exe"
            );

            if (!File.Exists(exePath))
                return "ERREUR : NetMonitor.Service.exe introuvable.";

            return RunAdmin(
                "schtasks /Create /TN \"MCEMonitor_NetMonitorService\" " +
                "/SC ONSTART " +
                $"/TR \"\\\"{exePath}\\\"\" /RU SYSTEM /RL HIGHEST /F"
            );
        }

        public static string DeleteNetMonitorTask()
            => RunAdmin("schtasks /Delete /TN \"MCEMonitor_NetMonitorService\" /F");

        public static bool NetMonitorTaskExists()
            => QueryTask("MCEMonitor_NetMonitorService");

        // ============================================================
        // STOP MONITOR
        // ============================================================
        public static string CreateStopTask()
        {
            string exe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "StopMonitor.exe"
            );

            StringBuilder sb = new StringBuilder();

            sb.AppendLine(RunAdmin(
                "schtasks /Create /TN \"MCEMonitor_StopMonitor_Shutdown\" " +
                "/SC ONEVENT /EC System " +
                "/MO \"*[System[(EventID=1074 or EventID=6006 or EventID=6008)]]\" " +
                $"/TR \"\\\"{exe}\\\" shutdown\" /RU SYSTEM /RL HIGHEST /F"
            ));

            sb.AppendLine(RunAdmin(
                "schtasks /Create /TN \"MCEMonitor_StopMonitor_Boot\" " +
                "/SC ONSTART " +
                $"/TR \"\\\"{exe}\\\" boot\" /RU SYSTEM /RL HIGHEST /F"
            ));

            return sb.ToString();
        }

        public static string DeleteStopTask()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(RunAdmin("schtasks /Delete /TN \"MCEMonitor_StopMonitor_Shutdown\" /F"));
            sb.AppendLine(RunAdmin("schtasks /Delete /TN \"MCEMonitor_StopMonitor_Boot\" /F"));
            return sb.ToString();
        }

        public static bool StopTaskExists()
            => QueryTask("MCEMonitor_StopMonitor_Shutdown") ||
               QueryTask("MCEMonitor_StopMonitor_Boot");

        // ============================================================
        // OUTILS COMMUNS
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

                Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                Task<string> errTask = p.StandardError.ReadToEndAsync();

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
            const string exe = "schtasks.exe";

            // Extraire les arguments après "schtasks" si présents
            string realArgs = args.StartsWith("schtasks ", StringComparison.OrdinalIgnoreCase)
                ? args.Substring("schtasks ".Length)
                : args;

            bool isElevated = IsProcessElevated();
            bool needUac    = !isElevated;

            var psi = new ProcessStartInfo
            {
                FileName               = exe,
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

            try
            {
                using var p = Process.Start(psi);
                if (p == null) return "ERREUR : démarrage schtasks impossible.";

                if (needUac)
                {
                    // Mode UAC : pas de capture possible
                    if (!p.WaitForExit(ADMIN_TIMEOUT_MS))
                    {
                        try { p.Kill(true); } catch { }
                        return "ERREUR : timeout UAC/schtasks (30s).";
                    }

                    return p.ExitCode == 0
                        ? "OK"
                        : $"ERREUR : schtasks code {p.ExitCode}.";
                }

                // Mode capturé
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