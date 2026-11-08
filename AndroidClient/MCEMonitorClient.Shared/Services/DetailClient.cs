using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCEMonitorClient.Shared.Models;

namespace MCEMonitorClient.Shared.Services
{
    /// <summary>
    /// Récupère les détails complets d'un serveur via /api/detail.
    /// Endpoint appelé uniquement à l'ouverture de ServerDetailPage
    /// et lors du pull-to-refresh — pas en continu.
    /// </summary>
    public class DetailClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<SystemDetailDto?> FetchSystemDetailAsync(
            ServerEntry server,
            CancellationToken ct = default)
        {
            if (server == null || string.IsNullOrWhiteSpace(server.FullUrl))
                return null;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

                // Basic Auth manuelle (Credentials/PreAuthenticate ne marchent pas sur Android)
                if (!string.IsNullOrEmpty(server.Username))
                {
                    string raw = $"{server.Username}:{server.Password}";
                    string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Basic", b64);
                }

                string url = $"{server.FullUrl}/api/detail";
                using var resp = await http.GetAsync(url, ct);

                if (!resp.IsSuccessStatusCode)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[DETAIL] {server.Name} -> HTTP {(int)resp.StatusCode}");
                    return null;
                }

                string json = await resp.Content.ReadAsStringAsync(ct);

                var dto = JsonSerializer.Deserialize<SystemDetailDto>(json, JsonOptions);
                return dto;
            }
            catch (TaskCanceledException)
            {
                System.Diagnostics.Trace.WriteLine($"[DETAIL] {server.Name} -> timeout");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[DETAIL] {server.Name} -> {ex.Message}");
                return null;
            }
        }
    }
}