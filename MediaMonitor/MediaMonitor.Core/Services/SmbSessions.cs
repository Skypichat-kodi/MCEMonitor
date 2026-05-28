using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

public class SmbSession
{
    public ulong SessionId { get; set; }
    public string ClientComputerName { get; set; } = "";
    public string ClientIPAddress { get; set; } = "";
    public string Username { get; set; } = "";
}

public static class SmbSessions
{
    public static List<SmbSession> GetSessions(string serverName)
    {
        var result = new List<SmbSession>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    "-NoProfile -Command \"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
                    "Get-SmbSession | Select-Object SessionId,ClientComputerName,ClientIPAddress,Username | ConvertTo-Json -Depth 4\"",
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
                var item = JsonSerializer.Deserialize<SmbSession>(output);
                if (item != null)
                    result.Add(item);
            }
            else
            {
                var items = JsonSerializer.Deserialize<List<SmbSession>>(output);
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

