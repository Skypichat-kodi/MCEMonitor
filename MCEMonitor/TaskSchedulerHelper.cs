using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MCEMonitor.Utils
{
    public static class TaskSchedulerHelper
    {
        // ============================================================
        // WAKE MONITOR
        // ============================================================

        public static string CreateWakeTask()
        {
            // Remplacement : WakeMonitor.exe au lieu de MCEMonitor.exe
            string exe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "WakeMonitor.exe"
            );

            return RunAdmin(
                "schtasks /Create /TN \"MCEMonitor_Wake\" " +
                "/SC ONEVENT /EC System /MO \"*[System/EventID=1]\" " +
                $"/TR \"\\\"{exe}\\\"\" /RU SYSTEM /RL HIGHEST /F"
            );
        }

        public static string DeleteWakeTask()
        {
            return RunAdmin("schtasks /Delete /TN \"MCEMonitor_Wake\" /F");
        }

        public static bool WakeTaskExists()
        {
            return QueryTask("MCEMonitor_Wake");
        }


        // ============================================================
        // MEDIA MONITOR — SERVICE (DÉMARRAGE AUTOMATIQUE)
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
        {
            return RunAdmin("schtasks /Delete /TN \"MCEMonitor_MediaMonitorService\" /F");
        }

        public static bool MediaMonitorServiceTaskExists()
        {
            return QueryTask("MCEMonitor_MediaMonitorService");
        }


        // ============================================================
        // SHUTDOWN (ON / OFF)
        // ============================================================

        public static string CreateShutdownTask(int hour, int minute, string mode)
        {
            string action = mode == "sleep"
                ? "rundll32.exe powrprof.dll,SetSuspendState 0,1,0"
                : "shutdown.exe /s /f /t 0";

            return RunAdmin(
                "schtasks /Create /TN \"MCEMonitor_Shutdown\" " +
                "/SC DAILY /ST " + $"{hour:D2}:{minute:D2} " +
                $"/TR \"{action}\" /RU SYSTEM /RL HIGHEST /F"
            );
        }

        public static string DeleteShutdownTask()
        {
            return RunAdmin("schtasks /Delete /TN \"MCEMonitor_Shutdown\" /F");
        }

        public static bool ShutdownTaskExists()
        {
            return QueryTask("MCEMonitor_Shutdown");
        }

        public static (int hour, int minute)? GetShutdownTaskTime()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/Query /TN \"MCEMonitor_Shutdown\" /V /FO LIST",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding(850)
                });

                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                foreach (var line in output.Split('\n'))
                {
                    if (line.Trim().StartsWith("Heure de début", StringComparison.OrdinalIgnoreCase))
                    {
                        string time = line.Split(':')[1].Trim();
                        string[] parts = time.Split(':');

                        return (int.Parse(parts[0]), int.Parse(parts[1]));
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ? REMPLACEMENT : version fiable et universelle
        public static string GetShutdownTaskMode()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/Query /TN \"MCEMonitor_Shutdown\" /V /FO LIST",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding(850)
                });

                string output = p.StandardOutput.ReadToEnd().ToLower();
                p.WaitForExit();

                // VERSION 100% UNIVERSELLE : on ignore la langue et on détecte la commande
                foreach (var line in output.Split('\n'))
                {
                    string l = line.ToLower();

                    // Détection arrêt
                    if (l.Contains("shutdown.exe"))
                        return "shutdown";

                    // Détection veille
                    if (l.Contains("rundll32.exe") && l.Contains("setsuspendstate"))
                        return "sleep";
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ============================================================
        // STOP MONITOR
        // ============================================================

        public static string CreateStopTask()
        {
            // Remplacement : StopMonitor.exe au lieu de MCEMonitor.exe
            string exe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor",
                "StopMonitor.exe"
            );

            return RunAdmin(
                "schtasks /Create /TN \"MCEMonitor_StopMonitor\" " +
                "/SC ONEVENT /EC System " +
                "/MO \"*[System[(EventID=1074 or EventID=6006 or EventID=6008)]]\" " +
                $"/TR \"\\\"{exe}\\\"\" /RU SYSTEM /RL HIGHEST /F"
            );
        }

        public static string DeleteStopTask()
        {
            return RunAdmin("schtasks /Delete /TN \"MCEMonitor_StopMonitor\" /F");
        }

        public static bool StopTaskExists()
        {
            return QueryTask("MCEMonitor_StopMonitor");
        }


        // ============================================================
        // OUTILS COMMUNS
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

            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();

                return string.IsNullOrWhiteSpace(error) ? output : error;
            }
        }
    }
}

