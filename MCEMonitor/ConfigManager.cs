using System.IO;
using System.Text.Json;

namespace MCEMonitor.Utils
{
    public static class ConfigManager
    {
        public static T Load<T>(string file) where T : new()
        {
            string path = Path.Combine(AppData.BasePath, file);
            if (!File.Exists(path)) return new T();

            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }

        public static void Save<T>(string file, T data)
        {
            string path = Path.Combine(AppData.BasePath, file);
            File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

