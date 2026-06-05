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

            var process = Process.Start(psi);
            if (process == null)
                return result;

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (string.IsNullOrWhiteSpace(output))
                return result;

            if (output.TrimStart().StartsWith("{"))
            {
                var item = JsonSerializer.Deserialize<SmbOpenFile>(output);
                if (item != null)
                    result.Add(item);
            }
            else
            {
                var items = JsonSerializer.Deserialize<List<SmbOpenFile>>(output);
                if (items != null)
                    result.AddRange(items);
            }
        }
        catch
        {
        }

        return result;
    }
}

