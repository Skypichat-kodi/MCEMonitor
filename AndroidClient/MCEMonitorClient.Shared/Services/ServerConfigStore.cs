using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MCEMonitorClient.Shared.Models;
using Microsoft.Maui.Storage;

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

        private static string UserKey(string serverId) => $"mcem_server_{serverId}_username";
        private static string PassKey(string serverId) => $"mcem_server_{serverId}_password";
        private const string KnownIdsPrefKey = "mcem_known_server_ids";

        public ServerConfigStore(string configPath)
        {
            _configPath = configPath;
        }

        public string ConfigPath => _configPath;

        // ============================================================
        //  Chargement
        // ============================================================
        public async Task<ServersConfig> LoadAsync()
        {
            ServersConfig config;

            try
            {
                if (!File.Exists(_configPath))
                    return new ServersConfig();

                string json = await File.ReadAllTextAsync(_configPath);

                // 1) Migration one-shot : si l'ancien JSON contient encore
                //    des identifiants en clair, on les bascule dans SecureStorage.
                await MigrateFromLegacyJsonIfNeededAsync(json);

                // 2) Désérialiser normalement (les [JsonIgnore] empêchent
                //    Username/Password d'apparaître)
                config = JsonSerializer.Deserialize<ServersConfig>(json, JsonOptions)
                         ?? new ServersConfig();
            }
            catch
            {
                return new ServersConfig();
            }

            // 3) Injecter les identifiants depuis SecureStorage (en parallèle)
            var tasks = config.Servers.Select(async s =>
            {
                try
                {
                    s.Username = await SecureStorage.Default.GetAsync(UserKey(s.Id)) ?? "";
                    s.Password = await SecureStorage.Default.GetAsync(PassKey(s.Id)) ?? "";
                }
                catch
                {
                    s.Username = "";
                    s.Password = "";
                }
            }).ToList();

            await Task.WhenAll(tasks);

            return config;
        }

        // ============================================================
        //  Sauvegarde
        // ============================================================
        public async Task<bool> SaveAsync(ServersConfig config)
        {
            try
            {
                // 1) Écrire les identifiants dans SecureStorage
                foreach (var s in config.Servers)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(s.Username) && string.IsNullOrEmpty(s.Password))
                        {
                            SecureStorage.Default.Remove(UserKey(s.Id));
                            SecureStorage.Default.Remove(PassKey(s.Id));
                        }
                        else
                        {
                            await SecureStorage.Default.SetAsync(UserKey(s.Id), s.Username);
                            await SecureStorage.Default.SetAsync(PassKey(s.Id), s.Password);
                        }
                    }
                    catch
                    {
                        // Un échec ponctuel ne bloque pas les autres serveurs
                    }
                }

                // 2) Nettoyer les identifiants orphelins (serveurs supprimés)
                CleanOrphanCredentials(config);

                // 3) Écrire le JSON (sans identifiants grâce aux [JsonIgnore])
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(config, JsonOptions);
                await File.WriteAllTextAsync(_configPath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ============================================================
        //  Migration one-shot de l'ancien JSON (identifiants en clair)
        // ============================================================
        private async Task MigrateFromLegacyJsonIfNeededAsync(string json)
        {
            var legacyCredentials = new List<(string Id, string User, string Pass)>();

            try
            {
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("servers", out var serversProp)
                    && serversProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in serversProp.EnumerateArray())
                    {
                        if (!s.TryGetProperty("id", out var idProp))
                            continue;

                        string id = idProp.GetString() ?? "";
                        if (string.IsNullOrEmpty(id))
                            continue;

                        string user = "";
                        string pass = "";

                        if (s.TryGetProperty("username", out var u) && u.ValueKind == JsonValueKind.String)
                            user = u.GetString() ?? "";

                        if (s.TryGetProperty("password", out var p) && p.ValueKind == JsonValueKind.String)
                            pass = p.GetString() ?? "";

                        if (!string.IsNullOrEmpty(user) || !string.IsNullOrEmpty(pass))
                            legacyCredentials.Add((id, user, pass));
                    }
                }
            }
            catch
            {
                // JSON invalide : pas de migration, on continue
                return;
            }

            if (legacyCredentials.Count == 0)
                return;

            // Migrer vers SecureStorage
            try
            {
                foreach (var (id, user, pass) in legacyCredentials)
                {
                    if (!string.IsNullOrEmpty(user))
                        await SecureStorage.Default.SetAsync(UserKey(id), user);
                    if (!string.IsNullOrEmpty(pass))
                        await SecureStorage.Default.SetAsync(PassKey(id), pass);
                }

                // Réécrire le JSON sans les identifiants
                var config = JsonSerializer.Deserialize<ServersConfig>(json, JsonOptions)
                             ?? new ServersConfig();

                // Réinjecter les identifiants en mémoire pour que SaveAsync
                // les ré-écrive dans SecureStorage (idempotent)
                foreach (var s in config.Servers)
                {
                    var match = legacyCredentials.FirstOrDefault(c => c.Id == s.Id);
                    if (match.Id != null)
                    {
                        s.Username = match.User;
                        s.Password = match.Pass;
                    }
                }

                await SaveAsync(config);
            }
            catch
            {
                // Migration échouée : on laisse l'ancien JSON en place,
                // l'utilisateur re-saisira ses identifiants si besoin.
            }
        }

        // ============================================================
        //  Nettoyage des identifiants orphelins
        // ============================================================
        private static void CleanOrphanCredentials(ServersConfig config)
        {
            try
            {
                string previousRaw = Preferences.Default.Get(KnownIdsPrefKey, "");
                var previousIds = previousRaw
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet();

                var currentIds = config.Servers.Select(s => s.Id).ToHashSet();

                foreach (var orphan in previousIds.Except(currentIds))
                {
                    try
                    {
                        SecureStorage.Default.Remove(UserKey(orphan));
                        SecureStorage.Default.Remove(PassKey(orphan));
                    }
                    catch { }
                }

                Preferences.Default.Set(KnownIdsPrefKey, string.Join(",", currentIds));
            }
            catch { }
        }
    }
}