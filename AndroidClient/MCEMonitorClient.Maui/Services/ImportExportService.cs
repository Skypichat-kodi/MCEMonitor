using System.Text;
using System.Text.Json;
using CommunityToolkit.Maui.Storage;
using MCEMonitorClient.Shared.Models;

namespace MCEMonitorClient.Maui.Services
{
    public static class ImportExportService
    {
        private const string FileName = "MCEMonitorClient.config";

        // ============================================================
        //  Export (avec identifiants, avertissement utilisateur)
        // ============================================================
        public static async Task<bool> ExportAsync()
        {
            try
            {
                var config = await ConfigService.LoadAsync();

                var page = Application.Current?.MainPage;
                if (page != null)
                {
                    bool ok = await page.DisplayAlert(
                        "Export",
                        "Le fichier contiendra les identifiants des serveurs en clair.\n\n" +
                        "Stockez-le en lieu sûr et ne le partagez pas.",
                        "Continuer", "Annuler");

                    if (!ok) return false;
                }

                string json = SerializeWithCredentials(config);

                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                var result = await FileSaver.Default.SaveAsync(FileName, stream, CancellationToken.None);

                return result.IsSuccessful;
            }
            catch
            {
                return false;
            }
        }

        // ============================================================
        //  Import (identifiants extraits du JSON brut)
        // ============================================================
        public static async Task<ServersConfig?> ImportAsync()
        {
            try
            {
                var pickResult = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Sélectionner un fichier de configuration"
                });

                if (pickResult == null)
                    return null;

                using var stream = await pickResult.OpenReadAsync();
                using var reader = new StreamReader(stream);
                string json = await reader.ReadToEndAsync();

                var config = JsonSerializer.Deserialize<ServersConfig>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new ServersConfig();

                // Extraire les identifiants du JSON brut
                // (les [JsonIgnore] les filtrent lors de la désérialisation)
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("servers", out var serversProp)
                        && serversProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sJson in serversProp.EnumerateArray())
                        {
                            if (!sJson.TryGetProperty("id", out var idProp))
                                continue;

                            string id = idProp.GetString() ?? "";
                            if (string.IsNullOrEmpty(id)) continue;

                            var entry = config.Servers.FirstOrDefault(s => s.Id == id);
                            if (entry == null) continue;

                            if (sJson.TryGetProperty("username", out var u) && u.ValueKind == JsonValueKind.String)
                                entry.Username = u.GetString() ?? "";

                            if (sJson.TryGetProperty("password", out var p) && p.ValueKind == JsonValueKind.String)
                                entry.Password = p.GetString() ?? "";
                        }
                    }
                }
                catch { }

                return config;
            }
            catch
            {
                return null;
            }
        }

        // ============================================================
        //  Sérialisation custom avec identifiants (pour l'export)
        // ============================================================
        private static string SerializeWithCredentials(ServersConfig config)
        {
            var exportModel = new
            {
                pollIntervalSeconds = config.PollIntervalSeconds,
                notifyOnStateChange = config.NotifyOnStateChange,
                playSoundOnCritical = config.PlaySoundOnCritical,
                servers = config.Servers.Select(s => new
                {
                    id          = s.Id,
                    name        = s.Name,
                    baseUrl     = s.BaseUrl,
                    port        = s.Port,
                    username    = s.Username,
                    password    = s.Password,
                    serviceType = s.ServiceType,
                    enabled     = s.Enabled
                }).ToList()
            };

            return JsonSerializer.Serialize(exportModel, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
    }
}