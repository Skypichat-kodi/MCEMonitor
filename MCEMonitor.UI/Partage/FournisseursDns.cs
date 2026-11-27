using System.Collections.Generic;
using System.Linq;

namespace MCEMonitor.UI.Partage
{
    /// <summary>
    /// Un champ demandé par un fournisseur DNS : une variable d'environnement
    /// attendue par lego, et ce qu'il faut afficher pour la saisir.
    /// </summary>
    public sealed class ChampDns
    {
        /// <summary>Nom exact de la variable d'environnement lue par lego.</summary>
        public string Cle { get; }

        /// <summary>Libellé affiché. C'est une clé de traduction.</summary>
        public string Libelle { get; }

        /// <summary>Exemple de valeur, affiché en gris.</summary>
        public string Exemple { get; }

        /// <summary>Vrai pour un secret : jamais réaffiché en clair dans la liste.</summary>
        public bool Secret { get; }

        /// <summary>Valeur pré-remplie pour un champ non secret (ex. « ovh-eu »).</summary>
        public string Defaut { get; }

        public ChampDns(string cle, string libelle, string exemple = "", bool secret = true, string defaut = "")
        {
            Cle = cle;
            Libelle = libelle;
            Exemple = exemple;
            Secret = secret;
            Defaut = defaut;
        }
    }

    /// <summary>
    /// Un fournisseur DNS que lego sait piloter. L'identifiant est celui attendu
    /// par « lego --dns » ; les champs sont les variables d'environnement qu'il lit.
    ///
    /// Les noms de variables et les identifiants viennent de la documentation
    /// officielle de lego, vérifiés fournisseur par fournisseur. En ajouter un
    /// tient en une entrée ici, sans rien changer ailleurs.
    /// </summary>
    public sealed class FournisseurDns
    {
        /// <summary>Identifiant lego, passé à « --dns ».</summary>
        public string Id { get; }

        /// <summary>Nom affiché dans la liste. Nom propre, non traduit.</summary>
        public string Nom { get; }

        /// <summary>Page où trouver les clés.</summary>
        public string Aide { get; }

        public IReadOnlyList<ChampDns> Champs { get; }

        public FournisseurDns(string id, string nom, string aide, params ChampDns[] champs)
        {
            Id = id;
            Nom = nom;
            Aide = aide;
            Champs = champs;
        }
    }

    /// <summary>Catalogue des fournisseurs DNS proposés.</summary>
    public static class FournisseursDns
    {
        public static IReadOnlyList<FournisseurDns> Tous { get; } = new List<FournisseurDns>
        {
            new("cloudflare", "Cloudflare",
                "https://dash.cloudflare.com/profile/api-tokens",
                new ChampDns("CLOUDFLARE_DNS_API_TOKEN", "Jeton d’API (DNS:Edit)", "jeton créé dans les profils API")),

            new("ovh", "OVH",
                "https://www.ovh.com/manager/",
                new ChampDns("OVH_APPLICATION_KEY", "Application key", "clé d’application"),
                new ChampDns("OVH_APPLICATION_SECRET", "Application secret", "secret d’application"),
                new ChampDns("OVH_CONSUMER_KEY", "Consumer key", "consumer key"),
                new ChampDns("OVH_ENDPOINT", "Endpoint", "ovh-eu", secret: false, defaut: "ovh-eu")),

            new("gandi", "Gandi",
                "https://account.gandi.net/",
                new ChampDns("GANDI_API_KEY", "Clé d’API", "clé d’API Gandi")),

            new("namecheap", "Namecheap",
                "https://ap.www.namecheap.com/settings/tools/apiaccess/",
                new ChampDns("NAMECHEAP_API_USER", "Utilisateur d’API", "nom d’utilisateur", secret: false),
                new ChampDns("NAMECHEAP_API_KEY", "Clé d’API", "clé d’API")),

            new("desec", "deSEC",
                "https://desec.io/tokens",
                new ChampDns("DESEC_TOKEN", "Jeton de domaine", "x-…")),

            new("hetzner", "Hetzner",
                "https://dns.hetzner.com/settings/api-token",
                new ChampDns("HETZNER_API_TOKEN", "Jeton d’API", "jeton Hetzner"))
        };

        /// <summary>Fournisseur par identifiant ; le premier de la liste si inconnu.</summary>
        /// <remarks>
        /// Comparaison insensible à la casse : un fichier écrit à la main, ou par
        /// une version antérieure, peut porter « OVH » plutôt que « ovh ».
        /// </remarks>
        public static FournisseurDns ParId(string? id)
            => Tous.FirstOrDefault(f => string.Equals(f.Id, id, System.StringComparison.OrdinalIgnoreCase)) ?? Tous[0];
    }
}
