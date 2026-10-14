using System;
using System.Collections.Generic;
using System.Management;
using MediaMonitor.Core.Services;

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
            string scopePath = string.IsNullOrWhiteSpace(serverName)
                ? @"\\.\root\Microsoft\Windows\SMB"
                : $@"\\{serverName}\root\Microsoft\Windows\SMB";

            var scope = new ManagementScope(scopePath);
            scope.Connect();

            // ClientIPAddress n'existe PAS dans MSFT_SmbSession ? retirée de la requête
            var query = new ObjectQuery(
                "SELECT SessionId, ClientComputerName, ClientUserName " +
                "FROM MSFT_SmbSession");

            using var searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject obj in searcher.Get())
            {
                try
                {
                    result.Add(new SmbSession
                    {
                        SessionId          = Convert.ToUInt64(obj["SessionId"] ?? 0UL),
                        ClientComputerName = obj["ClientComputerName"]?.ToString() ?? "",
                        ClientIPAddress    = "",   // propriété inexistante en WMI — laissée vide
                        Username           = obj["ClientUserName"]?.ToString() ?? ""
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
            CoreLog.Write("SMB SESSIONS ERROR: " + ex.Message);
        }

        // ----- DIAGNOSTIC -----
        CoreLog.Write($"DEBUG SMB Sessions: {result.Count} session(s)");
        foreach (var s in result)
            CoreLog.Write($"  -> SessionId={s.SessionId}, ClientComputerName='{s.ClientComputerName}', Username='{s.Username}'");

        return result;
    }
}