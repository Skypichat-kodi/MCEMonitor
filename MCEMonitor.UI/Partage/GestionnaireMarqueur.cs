using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCEMonitor.UI.Partage
{
    /// <summary>
    /// Une seule machine peut publier le domaine : la box ne redirige le 443 que
    /// vers une seule adresse, et deux installations se disputeraient les
    /// enregistrements A de la zone. Chaque passerelle déclare donc son propriétaire
    /// dans un enregistrement TXT, lu avant toute activation.
    ///
    /// Ce n'est pas une serrure : une zone modifiée à la main passerait outre. C'est
    /// un garde-fou, avec une porte de sortie — « prendre le relais » — sans laquelle
    /// la panne de la machine propriétaire bloquerait toutes les autres.
    /// </summary>
    public static class GestionnaireMarqueur
    {
        private const string ApiZones = "https://api.infomaniak.com/2/zones";

        /// <summary>
        /// Nom de l enregistrement TXT qui désigne le propriétaire. Surtout pas
        /// « _acme-challenge », où lego écrit et efface à chaque renouvellement.
        /// </summary>
        public const string NomMarqueur = "_passerelle";

        /// <summary>Machine qui détient la passerelle, et depuis quand.</summary>
        public sealed record Proprietaire(string Machine, string Date);

        /// <summary>Nom de la machine courante, tel qu il sera inscrit.</summary>
        public static string NomMachine
        {
            get
            {
                try { return Environment.MachineName; }
                catch { return "?"; }
            }
        }

        /// <summary>
        /// Propriétaire déclaré, ou null si le marqueur est absent, illisible, ou si
        /// le jeton manque. Un null n'est donc pas une preuve d absence : c'est à
        /// l appelant d en tenir compte.
        /// </summary>
        public static async Task<Proprietaire?> LireAsync(HttpsConfig config, CancellationToken jeton)
        {
            var trouve = await ChercherAsync(config, jeton);
            return trouve?.Proprietaire;
        }

        /// <summary>Inscrit la machine courante comme propriétaire du domaine.</summary>
        public static async Task EcrireAsync(HttpsConfig config, CancellationToken jeton)
        {
            string? token = Token(config);
            string zone = Zone(config);

            if (token == null || zone.Length == 0) return;

            string valeur = $"{NomMachine} ; {DateTime.Now:yyyy-MM-dd HH:mm}";
            var existant = await ChercherAsync(config, jeton);

            using var client = Client(token);

            if (existant != null)
            {
                var corps = JsonSerializer.Serialize(new { target = valeur, ttl = 300 });

                var reponse = await client.PutAsync($"{ApiZones}/{zone}/records/{existant.Id}",
                    new StringContent(corps, Encoding.UTF8, "application/json"), jeton);

                if (!reponse.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"Marqueur de passerelle non modifié : {(int)reponse.StatusCode}");

                CoreLog.Info("HTTPS", $"Marqueur de passerelle mis à jour : {valeur}.");
                return;
            }

            var creation = JsonSerializer.Serialize(new
            {
                source = NomMarqueur,
                target = valeur,
                type = "TXT",
                ttl = 300
            });

            var reponseCreation = await client.PostAsync($"{ApiZones}/{zone}/records",
                new StringContent(creation, Encoding.UTF8, "application/json"), jeton);

            if (!reponseCreation.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Marqueur de passerelle non créé : {(int)reponseCreation.StatusCode}");

            CoreLog.Info("HTTPS", $"Marqueur de passerelle créé : {valeur}.");
        }

        /// <summary>
        /// Efface le marqueur, mais seulement s il porte le nom de cette machine :
        /// une machine qui se désactive ne doit pas effacer la déclaration d une autre.
        /// </summary>
        public static async Task SupprimerAsync(HttpsConfig config, CancellationToken jeton)
        {
            string? token = Token(config);
            string zone = Zone(config);

            if (token == null || zone.Length == 0) return;

            var existant = await ChercherAsync(config, jeton);

            if (existant == null) return;

            if (!string.Equals(existant.Proprietaire.Machine, NomMachine, StringComparison.OrdinalIgnoreCase))
            {
                CoreLog.Warn("HTTPS",
                    $"Marqueur conservé : il appartient à {existant.Proprietaire.Machine}, pas à {NomMachine}.");
                return;
            }

            using var client = Client(token);

            var reponse = await client.DeleteAsync($"{ApiZones}/{zone}/records/{existant.Id}", jeton);

            if (reponse.IsSuccessStatusCode)
                CoreLog.Info("HTTPS", "Marqueur de passerelle effacé.");
            else
                CoreLog.Warn("HTTPS", $"Marqueur non effacé : {(int)reponse.StatusCode}.");
        }

        // ============================================================
        //  Lecture de l enregistrement
        // ============================================================
        private sealed record Trouve(int Id, Proprietaire Proprietaire);

        private static async Task<Trouve?> ChercherAsync(HttpsConfig config, CancellationToken jeton)
        {
            string? token = Token(config);
            string zone = Zone(config);

            if (token == null || zone.Length == 0) return null;

            using var client = Client(token);

            var reponse = await client.GetAsync($"{ApiZones}/{zone}/records?type=TXT&items_per_page=200", jeton);
            if (!reponse.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync(jeton));

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var ligne in data.EnumerateArray())
            {
                if (!ligne.TryGetProperty("id", out var id)) continue;
                if (!ligne.TryGetProperty("source", out var source)) continue;

                string nom = (source.GetString() ?? "").Trim();
                int point = nom.IndexOf('.');
                if (point > 0) nom = nom.Substring(0, point);

                if (!string.Equals(nom, NomMarqueur, StringComparison.OrdinalIgnoreCase)) continue;
                if (!ligne.TryGetProperty("target", out var target)) continue;

                // Infomaniak peut entourer la valeur de guillemets, comme pour tout TXT.
                string valeur = (target.GetString() ?? "").Trim().Trim('"').Trim();

                int separateur = valeur.IndexOf(';');

                return separateur > 0
                    ? new Trouve(id.GetInt32(),
                                 new Proprietaire(valeur.Substring(0, separateur).Trim(),
                                                  valeur.Substring(separateur + 1).Trim()))
                    : new Trouve(id.GetInt32(), new Proprietaire(valeur, ""));
            }

            return null;
        }

        private static HttpClient Client(string token)
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        private static string? Token(HttpsConfig config)
        {
            return config.Identifiants != null &&
                   config.Identifiants.TryGetValue("INFOMANIAK_ACCESS_TOKEN", out var t) &&
                   !string.IsNullOrWhiteSpace(t)
                ? t
                : null;
        }

        private static string Zone(HttpsConfig config)
            => (config.Domaine ?? "").Trim().TrimEnd('.').ToLowerInvariant();
    }
}
