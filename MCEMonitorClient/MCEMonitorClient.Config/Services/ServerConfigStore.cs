using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
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
                if (config == null) return new ServersConfig();

                DecoderMotsDePasse(config);
                return config;
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

                // Chiffrement avant écriture : aucun mot de passe en clair sur disque
                EncoderMotsDePasse(config);

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

        /// <summary>
        /// Déchiffre les mots de passe (DPAPI). Repli sur l'ancienne valeur en clair
        /// pour les fichiers écrits avant le chiffrement.
        /// </summary>
        private static void DecoderMotsDePasse(ServersConfig config)
        {
            if (config?.Servers == null) return;

            foreach (var s in config.Servers)
            {
                s.Password = !string.IsNullOrWhiteSpace(s.PasswordEnc)
                    ? (ProtegeIdentifiants.Dechiffrer(s.PasswordEnc) ?? "")
                    : s.PasswordLegacy;
            }
        }

        /// <summary>Chiffre les mots de passe avant écriture et efface la forme en clair.</summary>
        private static void EncoderMotsDePasse(ServersConfig config)
        {
            if (config?.Servers == null) return;

            foreach (var s in config.Servers)
            {
                s.PasswordEnc    = ProtegeIdentifiants.Chiffrer(s.Password);
                s.PasswordLegacy = "";
            }
        }
    }

    /// <summary>
    /// Chiffrement DPAPI des mots de passe de serveurs.
    ///
    /// Portée LocalMachine : la configuration est écrite par l'interface (compte
    /// utilisateur, élevé ou non) et relue par le service, qui tourne en SYSTEM
    /// (tâche planifiée /RU SYSTEM). CurrentUser rendrait la valeur indéchiffrable
    /// pour l'un des deux.
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