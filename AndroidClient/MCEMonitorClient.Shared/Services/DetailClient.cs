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
    public class DetailClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // ============================================================
        //  SystemMonitor
        // ============================================================
        public async Task<SystemDetailDto?> FetchSystemDetailAsync(
            ServerEntry server,
            CancellationToken ct = default)
        {
            return await FetchDetailAsync<SystemDetailDto>(server, "api/detail", ct);
        }

        // ============================================================
        //  RomMonitor
        // ============================================================
        public async Task<RomDetailDto?> FetchRomDetailAsync(
            ServerEntry server,
            CancellationToken ct = default)
        {
            return await FetchDetailAsync<RomDetailDto>(server, "api/detail", ct);
        }

        // ============================================================
        //  Méthode générique (mutualisée)
        // ============================================================
        private async Task<T?> FetchDetailAsync<T>(
            ServerEntry server,
            string path,
            CancellationToken ct) where T : class
        {
            if (server == null || string.IsNullOrWhiteSpace(server.FullUrl))
                return null;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

                if (!string.IsNullOrEmpty(server.Username))
                {
                    string raw = $"{server.Username}:{server.Password}";
                    string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Basic", b64);
                }

                string url = $"{server.FullUrl}/{path}";
                using var resp = await http.GetAsync(url, ct);

                if (!resp.IsSuccessStatusCode)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[DETAIL] {server.Name} {path} -> HTTP {(int)resp.StatusCode}");
                    return null;
                }

                string json = await resp.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (TaskCanceledException)
            {
                System.Diagnostics.Trace.WriteLine($"[DETAIL] {server.Name} {path} -> timeout");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[DETAIL] {server.Name} {path} -> {ex.Message}");
                return null;
            }
        }
    }
}