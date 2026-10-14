using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using MediaMonitor.Core.Services;

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
            string scopePath = string.IsNullOrWhiteSpace(serverName)
                ? @"\\.\root\Microsoft\Windows\SMB"
                : $@"\\{serverName}\root\Microsoft\Windows\SMB";

            var scope = new ManagementScope(scopePath);
            scope.Connect();

            var query = new ObjectQuery("SELECT SessionId, Path FROM MSFT_SmbOpenFile");
            using var searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject obj in searcher.Get())
            {
                try
                {
                    result.Add(new SmbOpenFile
                    {
                        SessionId = Convert.ToUInt64(obj["SessionId"] ?? 0UL),
                        Path      = obj["Path"]?.ToString() ?? ""
                    });
                }
                catch { }
                finally
                {
                    obj.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            CoreLog.Write("SMB OPENFILES ERROR: " + ex.Message);
        }

        // ----- DIAGNOSTIC -----
        CoreLog.Write($"DEBUG SMB OpenFiles: {result.Count} fichier(s)");
        foreach (var f in result.Take(3))
            CoreLog.Write($"  -> SessionId={f.SessionId}, Path='{f.Path}'");

        return result;
    }
}