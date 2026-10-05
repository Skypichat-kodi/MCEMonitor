using System;
using System.IO;
using System.Text.Json;
using MCEMonitorClient.Tray.Models;

namespace MCEMonitorClient.Tray.Services
{
    public static class ServerConfigStore
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor"
        );

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "MCEMonitorClient.config");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static ServersConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return new ServersConfig();

                string json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<ServersConfig>(json, JsonOptions);

                return config ?? new ServersConfig();
            }
            catch
            {
                return new ServersConfig();
            }
        }

        public static string GetConfigPath() => ConfigPath;
    }
}