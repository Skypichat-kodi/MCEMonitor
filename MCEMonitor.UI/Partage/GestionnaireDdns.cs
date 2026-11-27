using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCEMonitor.UI.Partage
{
    /// <summary>
    /// Maintient à jour l'enregistrement A des sous-domaines publiés.
    ///
    /// Remplace le DDNS d'Afraid.org : l'IP publique change, le nom doit suivre.
    /// Passe par l'API Infomaniak, avec le même jeton que le certificat — les
    /// portées dns:read et dns:write couvrent la lecture comme l'écriture.
    /// </summary>
    public static class GestionnaireDdns
    {
        private const string ApiZones = "https://api.infomaniak.com/2/zones";

        /// <summary>Adresse qui renvoie l'IP publique en texte brut.</summary>
        private const string ServiceIp = "https://api.ipify.org";

        /// <summary>
        /// Met à jour les enregistrements A des hôtes publiés, si nécessaire.
        /// </summary>
        /// <param name="journal">Messages d'avancement, un par hôte traité.</param>
        /// <returns>Nombre d'enregistrements effectivement modifiés.</returns>
        public static async Task<int> MettreAJourAsync(IProgress<string>? journal, CancellationToken jeton)
        {
            var config = HttpsConfig.Charger();

            if (!config.Actif || string.IsNullOrWhiteSpace(config.Domaine))
                return 0;

            var hotes = HotesAPublier(config);
            if (hotes.Count == 0) return 0;

            string? token = LireToken(config);
            if (token == null)
                throw new InvalidOperationException("Aucun jeton d'API n'est enregistré pour ce fournisseur.");

            string ip = await LireIpPubliqueAsync(jeton);
            journal?.Report($"IP publique : {ip}");

            string zone = config.Domaine.Trim().TrimEnd('.').ToLowerInvariant();
            int modifies = 0;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Les enregistrements A de la zone, une seule lecture pour tous les hôtes.
            var enregistrements = await LireEnregistrementsAAsync(client, zone, jeton);

            foreach (var hote in hotes)
            {
                string nom = hote;   // « net », ou « @ » pour la racine

                if (!enregistrements.TryGetValue(nom, out var existant))
                {
                    journal?.Report($"{hote} : aucun enregistrement A, création…");
                    await CreerAsync(client, zone, nom, ip, jeton);
                    modifies++;
                    continue;
                }

                if (string.Equals(existant.Ip, ip, StringComparison.Ordinal))
                {
                    journal?.Report($"{hote} : à jour ({ip})");
                    continue;
                }

                journal?.Report($"{hote} : {existant.Ip} → {ip}");
                await ModifierAsync(client, zone, existant.Id, ip, jeton);
                modifies++;
            }

            if (modifies > 0)
                CoreLog.Info("DDNS", $"{modifies} enregistrement(s) A mis à jour vers {ip}.");

            return modifies;
        }

        /// <summary>Hôtes à maintenir : tous les sous-domaines publiés, plus la racine.</summary>
        private static List<string> HotesAPublier(HttpsConfig config)
        {
            var hotes = new List<string>();

            foreach (var site in GenerateurCaddyfile.Sites(config))
            {
                // net.mcemonitor.org → « net »
                int point = site.Hote.IndexOf('.');
                if (point > 0) hotes.Add(site.Hote.Substring(0, point));
            }

            return hotes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string? LireToken(HttpsConfig config)
        {
            // Seul Infomaniak est géré pour la mise à jour DNS.
            return config.Identifiants.TryGetValue("INFOMANIAK_ACCESS_TOKEN", out var t) &&
                   !string.IsNullOrWhiteSpace(t) ? t : null;
        }

        private static async Task<string> LireIpPubliqueAsync(CancellationToken jeton)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            string ip = (await client.GetStringAsync(ServiceIp, jeton)).Trim();

            if (!System.Net.IPAddress.TryParse(ip, out _))
                throw new InvalidOperationException($"IP publique illisible : « {ip} »");

            return ip;
        }

        private sealed record EnregistrementA(int Id, string Ip);

        /// <summary>Enregistrements A de la zone, indexés par nom d'hôte.</summary>
        private static async Task<Dictionary<string, EnregistrementA>> LireEnregistrementsAAsync(
            HttpClient client, string zone, CancellationToken jeton)
        {
            var resultat = new Dictionary<string, EnregistrementA>(StringComparer.OrdinalIgnoreCase);

            var reponse = await client.GetAsync($"{ApiZones}/{zone}/records?type=A&items_per_page=200", jeton);
            reponse.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync(jeton));

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return resultat;

            foreach (var ligne in data.EnumerateArray())
            {
                if (!ligne.TryGetProperty("id", out var id)) continue;
                if (!ligne.TryGetProperty("source", out var source)) continue;
                if (!ligne.TryGetProperty("target", out var target)) continue;

                string nom = source.GetString() ?? "";
                string ip = target.GetString() ?? "";

                // Infomaniak peut renvoyer le nom complet : on ne garde que la partie gauche.
                int point = nom.IndexOf('.');
                if (point > 0) nom = nom.Substring(0, point);
                if (nom.Length == 0) nom = "@";

                resultat[nom] = new EnregistrementA(id.GetInt32(), ip);
            }

            return resultat;
        }

        private static async Task ModifierAsync(HttpClient client, string zone, int id, string ip,
                                                CancellationToken jeton)
        {
            var corps = JsonSerializer.Serialize(new { target = ip, ttl = 300 });

            var reponse = await client.PutAsync($"{ApiZones}/{zone}/records/{id}",
                new StringContent(corps, Encoding.UTF8, "application/json"), jeton);

            if (!reponse.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Modification refusée : {(int)reponse.StatusCode} "
                    + (await reponse.Content.ReadAsStringAsync(jeton)));
        }

        private static async Task CreerAsync(HttpClient client, string zone, string nom, string ip,
                                             CancellationToken jeton)
        {
            var corps = JsonSerializer.Serialize(new
            {
                source = nom,
                target = ip,
                type = "A",
                ttl = 300
            });

            var reponse = await client.PostAsync($"{ApiZones}/{zone}/records",
                new StringContent(corps, Encoding.UTF8, "application/json"), jeton);

            if (!reponse.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Création refusée : {(int)reponse.StatusCode} "
                    + (await reponse.Content.ReadAsStringAsync(jeton)));
        }
    }
}
