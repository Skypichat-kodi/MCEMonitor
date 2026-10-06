using MCEMonitorClient.Shared.Models;
using MCEMonitorClient.Shared.Services;

namespace MCEMonitorClient.Maui.Services
{
    public static class ConfigService
    {
        private static ServerConfigStore? _store;

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

        public static ServersConfig Load() => Store.Load();
        public static bool Save(ServersConfig config) => Store.Save(config);
    }
}