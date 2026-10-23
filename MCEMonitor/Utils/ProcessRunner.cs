using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace MCEMonitor.Common
{
    /// <summary>
    /// Helper robuste pour lancer un processus externe :
    /// - Timeout dur (jamais de blocage infini)
    /// - Lecture parallèle stdout/stderr (pas de deadlock)
    /// - Détection du mode élevé (UAC)
    /// - Kill propre en cas de timeout
    /// </summary>
    public static class ProcessRunner
    {
        public const int DefaultTimeoutMs = 5000;
        public const int AdminTimeoutMs   = 30000;

        /// <summary>
        /// Lance un processus et capture stdout/stderr avec timeout.
        /// </summary>
        /// <returns>(exitCode, stdout, stderr). exitCode = -1 si échec/timeout.</returns>
        public static (int exitCode, string output, string error) Run(
            string fileName,
            string arguments,
            int timeoutMs = DefaultTimeoutMs,
            bool runAsAdmin = false,
            Encoding encoding = null)
        {
            encoding ??= Encoding.GetEncoding(850);

            bool isElevated = IsProcessElevated();

            // Si on demande runas ET qu'on est déjà élevé, on peut capturer sans UAC.
            bool needUac = runAsAdmin && !isElevated;

            var psi = new ProcessStartInfo
            {
                FileName               = fileName,
                Arguments              = arguments,
                UseShellExecute        = needUac,          // obligatoire avec runas
                RedirectStandardOutput = !needUac,
                RedirectStandardError  = !needUac,
                CreateNoWindow         = !needUac,
                StandardOutputEncoding = needUac ? null : encoding,
                StandardErrorEncoding  = needUac ? null : encoding
            };

            if (needUac)
                psi.Verb = "runas";

            Process p;
            try
            {
                p = Process.Start(psi);
                if (p == null)
                    return (-1, "", "Process.Start a retourné null.");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return (-1, "", "UAC refusé par l'utilisateur.");
            }
            catch (Exception ex)
            {
                return (-1, "", ex.Message);
            }

            using (p)
            {
                // --- Mode UAC : pas de redirection possible ---
                if (needUac)
                {
                    if (!p.WaitForExit(AdminTimeoutMs))
                    {
                        try { p.Kill(true); } catch { }
                        return (-1, "", $"Timeout {AdminTimeoutMs} ms (UAC/schtasks).");
                    }
                    return (p.ExitCode, "", "");
                }

                // --- Mode capturé : lecture parallèle pour éviter le deadlock ---
                Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                Task<string> errTask = p.StandardError.ReadToEndAsync();

                bool exited = p.WaitForExit(timeoutMs);

                string output = SafeResult(outTask);
                string error  = SafeResult(errTask);

                if (!exited)
                {
                    try { p.Kill(true); } catch { }
                    return (-1, output, $"Timeout {timeoutMs} ms.");
                }

                return (p.ExitCode, output, error);
            }
        }

        /// <summary>
        /// Variante : lance un processus et retourne uniquement stdout (ou stderr si erreur).
        /// </summary>
        public static string RunSimple(
            string fileName,
            string arguments,
            int timeoutMs = DefaultTimeoutMs,
            bool runAsAdmin = false,
            Encoding encoding = null)
        {
            var (code, output, error) = Run(fileName, arguments, timeoutMs, runAsAdmin, encoding);

            if (code < 0)
                return string.IsNullOrWhiteSpace(error) ? "ERREUR." : "ERREUR : " + error;

            return string.IsNullOrWhiteSpace(error) ? output : error;
        }

        /// <summary>
        /// Indique si le processus courant est déjà élevé (admin).
        /// </summary>
        public static bool IsProcessElevated()
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