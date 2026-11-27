using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCEMonitor.UI.Partage
{
    /// <summary>
    /// Installe Caddy, le serveur qui termine le TLS et relaie vers les modules.
    ///
    /// Comme lego, il n'est pas livré avec MCEMonitor : il est téléchargé le jour
    /// où la passerelle est activée, depuis la release officielle. Sans module DNS,
    /// puisque le certificat est obtenu par lego et simplement lu depuis le disque.
    /// </summary>
    public static class TelechargeurCaddy
    {
        private const string DepotApi = "https://api.github.com/repos/caddyserver/caddy/releases/latest";

        public static readonly string CheminCaddy = Path.Combine(TelechargeurLego.DossierOutils, "caddy.exe");

        public static bool EstInstalle => File.Exists(CheminCaddy) && new FileInfo(CheminCaddy).Length > 5 * 1024 * 1024;

        public static async Task InstallerAsync(IProgress<double> progression, CancellationToken jeton)
        {
            Directory.CreateDirectory(TelechargeurLego.DossierOutils);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MCEMonitor", "4.0"));
            client.Timeout = TimeSpan.FromMinutes(5);

            string json = await client.GetStringAsync(DepotApi, jeton);
            string? urlArchive = null;
            string version = "?";

            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("tag_name", out var tag))
                    version = tag.GetString() ?? "?";

                if (doc.RootElement.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string? nom = asset.TryGetProperty("name", out var n) ? n.GetString() : null;

                        // caddy_2.10.0_windows_amd64.zip
                        if (nom != null && nom.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                            nom.Contains("windows", StringComparison.OrdinalIgnoreCase) &&
                            nom.Contains("amd64", StringComparison.OrdinalIgnoreCase) &&
                            !nom.Contains("custom", StringComparison.OrdinalIgnoreCase))
                        {
                            urlArchive = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(urlArchive))
                throw new InvalidOperationException($"Aucune archive Windows trouvée dans la release {version}.");

            CoreLog.Info("HTTPS", $"Téléchargement de Caddy {version} : {urlArchive}");

            string archiveTemp = Path.Combine(Path.GetTempPath(), "caddy_" + Guid.NewGuid().ToString("N") + ".zip");

            try
            {
                using (var reponse = await client.GetAsync(urlArchive, HttpCompletionOption.ResponseHeadersRead, jeton))
                {
                    reponse.EnsureSuccessStatusCode();

                    long total = reponse.Content.Headers.ContentLength ?? -1;
                    long recus = 0;

                    await using (var source = await reponse.Content.ReadAsStreamAsync(jeton))
                    await using (var cible = File.Create(archiveTemp))
                    {
                        var tampon = new byte[81920];
                        int lus;

                        while ((lus = await source.ReadAsync(tampon, jeton)) > 0)
                        {
                            await cible.WriteAsync(tampon.AsMemory(0, lus), jeton);
                            recus += lus;
                            if (total > 0) progression.Report(0.8 * recus / total);
                        }
                    }
                }

                progression.Report(0.85);

                using (var archive = ZipFile.OpenRead(archiveTemp))
                {
                    var entree = archive.Entries.FirstOrDefault(e =>
                        string.Equals(e.Name, "caddy.exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("L'archive ne contient pas caddy.exe.");

                    string caddyTemp = CheminCaddy + ".new";
                    entree.ExtractToFile(caddyTemp, overwrite: true);
                    File.Move(caddyTemp, CheminCaddy, overwrite: true);
                }

                progression.Report(1.0);
                CoreLog.Info("HTTPS", $"Caddy {version} installé dans {CheminCaddy}");
            }
            finally
            {
                try { if (File.Exists(archiveTemp)) File.Delete(archiveTemp); } catch { }
            }
        }
    }
}
