using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

public static class TimerDiagnostics
{
    private const int QUERY_TIMEOUT_MS = 15000;   // /query /v est plus lent que /query simple

    public static string GetInfo()
    {
        var sb = new StringBuilder();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "schtasks.exe",
                Arguments              = "/query /fo LIST /v",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.GetEncoding(850),
                StandardErrorEncoding  = Encoding.GetEncoding(850)
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                sb.AppendLine("ERREUR TimerDiagnostics : impossible de lancer schtasks.");
                return sb.ToString();
            }

            // Lecture parallèle stdout + stderr (évite le deadlock)
            Task<string> outTask = proc.StandardOutput.ReadToEndAsync();
            Task<string> errTask = proc.StandardError.ReadToEndAsync();

            // Timeout dur : jamais de blocage infini
            if (!proc.WaitForExit(QUERY_TIMEOUT_MS))
            {
                try { proc.Kill(true); } catch { }
                sb.AppendLine($"ERREUR TimerDiagnostics : timeout schtasks ({QUERY_TIMEOUT_MS} ms).");
                return sb.ToString();
            }

            string output = SafeResult(outTask);

            // On ne garde que les tâches qui peuvent réveiller la machine
            var lines = output.Split('\n');

            string currentTask = "";
            bool wakeToRun = false;
            string nextRun = "";

            foreach (var raw in lines)
            {
                string line = raw.Trim();

                if (line.StartsWith("TaskName:"))
                {
                    currentTask = line.Substring(9).Trim();
                    wakeToRun = false;
                    nextRun = "";
                }

                if (line.StartsWith("Next Run Time:"))
                    nextRun = line.Substring(15).Trim();

                if (line.StartsWith("Wake To Run:"))
                {
                    wakeToRun = line.Contains("Yes");

                    if (wakeToRun)
                    {
                        sb.AppendLine($"Tâche : {currentTask}");
                        sb.AppendLine($"  Prochaine exécution : {nextRun}");
                        sb.AppendLine($"  WakeToRun : Oui");
                        sb.AppendLine();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERREUR TimerDiagnostics : " + ex.Message);
        }

        return sb.ToString();
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