using System;
using System.IO;
using System.Text.Json;
using MCEMonitorClient.Config.Models;

namespace MCEMonitorClient.Config.Services
{
    /// <summary>
    /// Gère la lecture/écriture du fichier servers.json.
    /// </summary>
    public static class ServerConfigStore
    {
        // %ProgramData%\MCEMonitorClient\servers.json
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

        /// <summary>
        /// Charge la config depuis le disque. Crée un fichier par défaut si absent.
        /// </summary>
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

        /// <summary>
        /// Sauvegarde la config sur le disque.
        /// </summary>
        public static bool Save(ServersConfig config)
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);

                string json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(ConfigPath, json);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Chemin complet du fichier (pour debug).
        /// </summary>
        public static string GetConfigPath() => ConfigPath;
    }
}