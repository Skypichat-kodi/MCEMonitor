using MCEMonitorClient.Shared.Models;
using MCEMonitorClient.Shared.Services;

namespace MCEMonitorClient.Maui.Services
{
    public static class ConfigService
    {
        private static ServerConfigStore? _store;
        private static ServersConfig? _cached;
        private static readonly object _lock = new();

        public static ServerConfigStore Store
        {
            get
            {
                if (_store == null)
                {
                    string path = System.IO.Path.Combine(
                        FileSystem.AppDataDirectory,
                        "MCEMonitorClient.config");
                    _store = new ServerConfigStore(path);
                }
                return _store;
            }
        }

        public static async Task<ServersConfig> LoadAsync()
        {
            lock (_lock)
            {
                if (_cached != null)
                    return _cached;
            }

            var config = await Store.LoadAsync();

            lock (_lock)
            {
                _cached = config;
            }

            return config;
        }

        public static async Task<bool> SaveAsync(ServersConfig config)
        {
            bool ok = await Store.SaveAsync(config);

            if (ok)
            {
                lock (_lock)
                {
                    _cached = config;
                }
            }

            return ok;
        }

        /// <summary>
        /// Force le rechargement depuis le disque au prochain LoadAsync.
        /// À appeler si la config a été modifiée par un autre processus
        /// (par ex. par le ForegroundService).
        /// </summary>
        public static void InvalidateCache()
        {
            lock (_lock)
            {
                _cached = null;
            }
        }
    }
}