using MCEMonitorClient.Shared.Models;

namespace MCEMonitorClient.Maui.Pages;

public static class ServerDetailRouter
{
    public static async Task NavigateToDetailAsync(ServerEntry server)
    {
        if (server == null || string.IsNullOrEmpty(server.Id))
            return;

        string route = GetRouteForService(server.ServiceType, server.Id);
        if (!string.IsNullOrEmpty(route))
            await Shell.Current.GoToAsync(route);
    }

    public static string GetRouteForService(string? serviceType, string serverId)
    {
        string type = (serviceType ?? "").Trim().ToLowerInvariant();

        return type switch
        {
            "systemmonitor" => $"systemmonitor/detail?id={Uri.EscapeDataString(serverId)}",
            // "rommonitor"   => $"rommonitor/detail?id={...}",
            // "netmonitor"   => $"netmonitor/detail?id={...}",
            // "mediamonitor" => $"mediamonitor/detail?id={...}",
            _ => ""
        };
    }
}