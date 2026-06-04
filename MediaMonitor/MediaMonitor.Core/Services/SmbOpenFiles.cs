using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

public class SmbOpenFile
{
    public ulong SessionId { get; set; }
    public string Path { get; set; } = "";
}

public static class SmbOpenFiles
{
    public static List<SmbOpenFile> GetOpenFiles(string serverName)
    {
        var result = new List<SmbOpenFile>();

        Process? process = null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    "-NoProfile -Command \"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
                    "Get-SmbOpenFile | Select-Object SessionId,Path | ConvertTo-Json -Depth 4\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            process = new Process { StartInfo = psi };
            process.Start();

            // Lecture complète des flux pour éviter les deadlocks
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            // Timeout de sécurité : 3 secondes
            if (!process.WaitForExit(3000))
            {
                try { process.Kill(); } catch { }
                return result;
            }

            if (string.IsNullOrWhiteSpace(output))
                return result;

            // Un seul objet JSON
            if (output.TrimStart().StartsWith("{"))
            {
                var item = JsonSerializer.Deserialize<SmbOpenFile>(output);
                if (item != null)
                    result.Add(item);
            }
            else // Tableau JSON
            {
                var items = JsonSerializer.Deserialize<List<SmbOpenFile>>(output);
                if (items != null)
                    result.AddRange(items);
            }
        }
        catch
        {
            // On ignore les erreurs ici, comme dans ta version d’origine
        }
        finally
        {
            process?.Dispose();
        }

        return result;
    }
}

