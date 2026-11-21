using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
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

                return config == null ? new ServersConfig() : DecoderMotsDePasse(config);
            }
            catch
            {
                return new ServersConfig();
            }
        }

        public static string GetConfigPath() => ConfigPath;

        /// <summary>
        /// Déchiffre les mots de passe (DPAPI). Repli sur l'ancienne valeur en clair
        /// pour les fichiers écrits avant le chiffrement.
        /// </summary>
        private static ServersConfig DecoderMotsDePasse(ServersConfig config)
        {
            if (config?.Servers == null) return config!;

            foreach (var s in config.Servers)
            {
                s.Password = !string.IsNullOrWhiteSpace(s.PasswordEnc)
                    ? (ProtegeIdentifiants.Dechiffrer(s.PasswordEnc) ?? "")
                    : s.PasswordLegacy;
            }

            return config;
        }
    }

    /// <summary>
    /// Chiffrement DPAPI des mots de passe de serveurs.
    ///
    /// Portée LocalMachine : la configuration est écrite par l'interface (compte
    /// utilisateur) et relue par le service, qui tourne en SYSTEM (tâche planifiée
    /// /RU SYSTEM). CurrentUser rendrait la valeur indéchiffrable pour l'un des deux.
    ///
    /// L'entropie doit être identique dans les projets Config et Service.
    /// </summary>
    internal static class ProtegeIdentifiants
    {
        private static readonly byte[] Entropie = Encoding.UTF8.GetBytes("MCEMonitor.client.v1");

        public static string Chiffrer(string clair)
        {
            if (string.IsNullOrEmpty(clair)) return string.Empty;

            try
            {
                byte[] octets   = Encoding.UTF8.GetBytes(clair);
                byte[] proteges = ProtectedData.Protect(octets, Entropie, DataProtectionScope.LocalMachine);
                return Convert.ToBase64String(proteges);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Renvoie null si la valeur est vide ou indéchiffrable sur ce poste.</summary>
        public static string? Dechiffrer(string chiffre)
        {
            if (string.IsNullOrWhiteSpace(chiffre)) return null;

            try
            {
                byte[] proteges = Convert.FromBase64String(chiffre);
                byte[] octets   = ProtectedData.Unprotect(proteges, Entropie, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(octets);
            }
            catch
            {
                return null;
            }
        }
    }
}