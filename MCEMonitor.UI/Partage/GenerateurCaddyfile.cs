using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MCEMonitor.UI.Partage
{
    /// <summary>
    /// Écrit le fichier de configuration de Caddy à partir des réglages de l'onglet.
    ///
    /// Un site par sous-domaine renseigné, chacun avec le même certificat wildcard
    /// et un relais vers l'adresse du module. Caddy se charge de la redirection
    /// HTTP vers HTTPS dès qu'un site est déclaré avec un nom de domaine.
    /// </summary>
    public static class GenerateurCaddyfile
    {
        public static readonly string CheminCaddyfile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "Caddyfile");

        /// <summary>Un site : le nom complet et la cible à relayer.</summary>
        public sealed record Site(string Hote, string Cible);

        /// <summary>Liste les sites à publier, d'après les sous-domaines renseignés.</summary>
        public static List<Site> Sites(HttpsConfig config)
        {
            var sites = new List<Site>();
            string domaine = config.Domaine.Trim().TrimEnd('.').ToLowerInvariant();

            if (domaine.Length == 0) return sites;

            void Ajouter(string sousDomaine, string cible)
            {
                string sous = sousDomaine.Trim().Trim('.', ' ').ToLowerInvariant();
                string cible2 = cible.Trim();

                if (sous.Length == 0 || cible2.Length == 0) return;

                // Le champ tolère les deux formes : « net » comme
                // « net.mcemonitor.org ». Sans cela, un nom complet saisi par
                // l utilisateur donnait net.mcemonitor.org.mcemonitor.org.
                string hote = sous == domaine || sous.EndsWith("." + domaine, StringComparison.Ordinal)
                    ? sous
                    : $"{sous}.{domaine}";

                sites.Add(new Site(hote, cible2));
            }

            Ajouter(config.SousNetMonitor,    config.CibleNetMonitor);
            Ajouter(config.SousRomMonitor,    config.CibleRomMonitor);
            Ajouter(config.SousSystemMonitor, config.CibleSystemMonitor);
            Ajouter(config.SousMediaMonitor,  config.CibleMediaMonitor);

            return sites;
        }

        /// <summary>
        /// Compose le contenu du Caddyfile. Renvoie une chaîne vide s'il n'y a
        /// aucun site à publier : mieux vaut ne rien lancer qu'un serveur vide.
        /// </summary>
        public static string Construire(HttpsConfig config)
        {
            var sites = Sites(config);
            if (sites.Count == 0) return "";

            string domaine = config.Domaine.Trim().TrimEnd('.').ToLowerInvariant();
            string certificat = GestionnaireCertificat.CheminCertificat(domaine);
            string cle = Path.ChangeExtension(certificat, ".key");

            if (!File.Exists(certificat) || !File.Exists(cle))
                throw new InvalidOperationException("Le certificat est introuvable : obtenez-le avant de lancer la passerelle.");

            var lignes = new List<string>
            {
                "# Fichier généré par MCEMonitor — ne pas modifier à la main.",
                "# Régénéré à chaque enregistrement des réglages de l'accès distant.",
                "",
                "{",
                "\t# API locale : sert au rechargement après renouvellement du certificat.",
                "\tadmin 127.0.0.1:2019",
                "\tauto_https off",
                "}",
                ""
            };

            foreach (var site in sites)
            {
                lignes.Add($"{site.Hote} {{");

                // Certificat en fichier : Caddy ne le relit pas seul, d'où le
                // rechargement déclenché par MCEMonitor après un renouvellement.
                lignes.Add($"\ttls \"{certificat}\" \"{cle}\"");

                lignes.Add($"\treverse_proxy {site.Cible}");

                // L'interface est déjà protégée par mot de passe et OTP ; HSTS évite
                // qu'un navigateur accepte ensuite une connexion en clair.
                lignes.Add("\theader Strict-Transport-Security \"max-age=31536000; includeSubDomains\"");

                lignes.Add("}");
                lignes.Add("");
            }

            return string.Join(Environment.NewLine, lignes);
        }

        /// <summary>Écrit le Caddyfile. Renvoie faux si aucun site n'est à publier.</summary>
        public static bool Ecrire(HttpsConfig config)
        {
            string contenu = Construire(config);
            if (contenu.Length == 0) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(CheminCaddyfile)!);
            File.WriteAllText(CheminCaddyfile, contenu);

            CoreLog.Info("HTTPS", $"Caddyfile écrit : {Sites(config).Count} site(s).");
            return true;
        }
    }
}
