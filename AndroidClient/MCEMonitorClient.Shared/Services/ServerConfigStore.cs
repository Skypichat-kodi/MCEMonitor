using System;
using System.IO;
using System.Text.Json;
using MCEMonitorClient.Shared.Models;

namespace MCEMonitorClient.Shared.Services
{
    public class ServerConfigStore
    {
        private readonly string _configPath;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ServerConfigStore(string configPath)
        {
            _configPath = configPath;
        }

        public string ConfigPath => _configPath;

        public ServersConfig Load()
        {
            try
            {
                if (!File.Exists(_configPath))
                    return new ServersConfig();

                string json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<ServersConfig>(json, JsonOptions);

                return config ?? new ServersConfig();
            }
            catch
            {
                return new ServersConfig();
            }
        }

        public bool Save(ServersConfig config)
        {
            try
            {
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(_configPath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}