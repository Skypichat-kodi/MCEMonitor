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
    /// Installe lego, l'outil qui obtient et renouvelle les certificats.
    ///
    /// Le binaire n'est pas livré avec MCEMonitor : il est téléchargé depuis la
    /// dernière release officielle de go-acme/lego le jour où l'accès distant est
    /// configuré, ce qui garantit une version à jour plutôt qu'une copie figée
    /// dans un installeur.
    /// </summary>
    public static class TelechargeurLego
    {
        private const string DepotApi = "https://api.github.com/repos/go-acme/lego/releases/latest";

        public static readonly string DossierOutils = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "Tools");

        public static readonly string CheminLego = Path.Combine(DossierOutils, "lego.exe");

        /// <summary>Le binaire est-il déjà là ?</summary>
        public static bool EstInstalle => File.Exists(CheminLego) && new FileInfo(CheminLego).Length > 1024 * 1024;

        /// <summary>
        /// Télécharge la dernière version et dépose `lego.exe` dans le dossier d'outils.
        /// </summary>
        /// <param name="progression">Fraction de 0 à 1, puis 1 à la fin.</param>
        public static async Task InstallerAsync(IProgress<double> progression, CancellationToken jeton)
        {
            Directory.CreateDirectory(DossierOutils);

            using var client = new HttpClient();
            // GitHub exige un User-Agent sur son API.
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MCEMonitor", "4.0"));
            client.Timeout = TimeSpan.FromMinutes(5);

            // --- 1. Trouver l'archive de la dernière version ---
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

                        // Le nom varie selon les versions : lego_v4.21.0_windows_amd64.zip
                        if (nom != null && nom.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                            nom.Contains("windows", StringComparison.OrdinalIgnoreCase) &&
                            nom.Contains("amd64", StringComparison.OrdinalIgnoreCase))
                        {
                            urlArchive = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(urlArchive))
                throw new InvalidOperationException($"Aucune archive Windows trouvée dans la release {version}.");

            CoreLog.Info("HTTPS", $"Téléchargement de lego {version} : {urlArchive}");

            // --- 2. Télécharger, en rapportant la progression ---
            string archiveTemp = Path.Combine(Path.GetTempPath(), "lego_" + Guid.NewGuid().ToString("N") + ".zip");

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

                            // 0 à 0,8 : le téléchargement ; le reste pour l'extraction.
                            if (total > 0) progression.Report(0.8 * recus / total);
                        }
                    }
                }

                // --- 3. Extraire lego.exe ---
                progression.Report(0.85);

                using (var archive = ZipFile.OpenRead(archiveTemp))
                {
                    var entree = archive.Entries.FirstOrDefault(e =>
                        string.Equals(e.Name, "lego.exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("L'archive ne contient pas lego.exe.");

                    // Extraction dans un fichier temporaire, puis déplacement : un
                    // lego.exe déjà présent peut être verrouillé par un renouvellement.
                    string legoTemp = CheminLego + ".new";
                    entree.ExtractToFile(legoTemp, overwrite: true);
                    File.Move(legoTemp, CheminLego, overwrite: true);
                }

                progression.Report(1.0);
                CoreLog.Info("HTTPS", $"lego {version} installé dans {CheminLego}");
            }
            finally
            {
                try { if (File.Exists(archiveTemp)) File.Delete(archiveTemp); } catch { }
            }
        }
    }
}
