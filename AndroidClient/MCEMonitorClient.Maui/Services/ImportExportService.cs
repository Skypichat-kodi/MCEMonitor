using System.Text;
using System.Text.Json;
using CommunityToolkit.Maui.Storage;
using MCEMonitorClient.Shared.Models;

namespace MCEMonitorClient.Maui.Services
{
    public static class ImportExportService
    {
        private const string FileName = "MCEMonitorClient.config";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Exporte la config actuelle dans un fichier choisi par l'utilisateur.
        /// </summary>
        public static async Task<bool> ExportAsync()
        {
            try
            {
                var config = ConfigService.Load();
                string json = JsonSerializer.Serialize(config, JsonOptions);

                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                var result = await FileSaver.Default.SaveAsync(FileName, stream, CancellationToken.None);

                return result.IsSuccessful;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Importe une config depuis un fichier choisi par l'utilisateur.
        /// Renvoie null si annulé ou en erreur.
        /// </summary>
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
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return config;
            }
            catch
            {
                return null;
            }
        }
    }
}