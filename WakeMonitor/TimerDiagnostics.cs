using System;
using System.Diagnostics;
using System.Text;

public static class TimerDiagnostics
{
    public static string GetInfo()
    {
        var sb = new StringBuilder();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/query /fo LIST /v",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var proc = Process.Start(psi);
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

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
}

