using System;
using System.IO;
using System.Text.Json;
using MCEMonitorClient.Service.Models;

namespace MCEMonitorClient.Service.Services
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

                // Migration ancien format (baseUrl avec port intégré)
                if (config != null)
                {
                    foreach (var s in config.Servers)
                    {
                        if (string.IsNullOrWhiteSpace(s.BaseUrl)) continue;

                        try
                        {
                            var uri = new Uri(s.BaseUrl.StartsWith("http") ? s.BaseUrl : "http://" + s.BaseUrl);
                            if (!uri.IsDefaultPort && uri.Port > 0 && s.Port == 0)
                            {
                                s.Port = uri.Port;
                                s.BaseUrl = $"{uri.Scheme}://{uri.Host}";
                            }
                        }
                        catch { }
                    }
                }

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