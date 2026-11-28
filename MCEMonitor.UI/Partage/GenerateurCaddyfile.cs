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

        /// <summary>
        /// Aplatit un nom pour qu'il tienne dans un seul niveau de sous-domaine :
        /// « MCE-SALON » et « MCE SALON » donnent tous deux « mce-salon ». Un
        /// wildcard ne couvrant qu'un niveau, un nom composé casserait le certificat.
        /// </summary>
        public static string Slug(string valeur)
        {
            if (string.IsNullOrWhiteSpace(valeur)) return "";

            string decompose = valeur.Trim().ToLowerInvariant()
                .Normalize(System.Text.NormalizationForm.FormD);

            var propre = new System.Text.StringBuilder();

            foreach (char c in decompose)
            {
                // Accent détaché par la décomposition : on le jette.
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    == System.Globalization.UnicodeCategory.NonSpacingMark)
                    continue;

                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) propre.Append(c);
                else if (c == ' ' || c == '_' || c == '.' || c == '-') propre.Append('-');
            }

            return System.Text.RegularExpressions.Regex
                .Replace(propre.ToString(), "-{2,}", "-")
                .Trim('-');
        }

        /// <summary>
        /// Sous-domaines et cibles demandés, dans l'ordre : d'abord les quatre
        /// modules de cette machine, puis les autres machines du réseau.
        /// </summary>
        private static IEnumerable<(string Sous, string Cible)> Candidats(HttpsConfig config)
        {
            if (config.PublierNetMonitor)    yield return (config.SousNetMonitor,    config.CibleNetMonitor);
            if (config.PublierRomMonitor)    yield return (config.SousRomMonitor,    config.CibleRomMonitor);
            if (config.PublierSystemMonitor) yield return (config.SousSystemMonitor, config.CibleSystemMonitor);
            if (config.PublierMediaMonitor)  yield return (config.SousMediaMonitor,  config.CibleMediaMonitor);

            foreach (var serveur in config.ServeursSupplementaires)
            {
                string machine = Slug(serveur.Machine);
                string ip = serveur.Ip.Trim();

                if (machine.Length == 0 || ip.Length == 0) continue;

                foreach (var module in serveur.Modules)
                {
                    string prefixe = Slug(module.Module);
                    if (prefixe.Length == 0 || module.Port <= 0) continue;

                    // « netmonitor-mce-salon » plutôt que « mce-salon.netmonitor » :
                    // un seul niveau, couvert par *.mcemonitor.org.
                    yield return ($"{prefixe}-{machine}", $"{ip}:{module.Port}");
                }
            }
        }

        /// <summary>Liste les sites à publier, d'après les sous-domaines renseignés.</summary>
        public static List<Site> Sites(HttpsConfig config)
        {
            var sites = new List<Site>();
            string domaine = config.Domaine.Trim().TrimEnd('.').ToLowerInvariant();

            if (domaine.Length == 0) return sites;

            foreach (var (sousDomaine, cibleBrute) in Candidats(config))
            {
                string sous = sousDomaine.Trim().Trim('.', ' ').ToLowerInvariant();
                string cible = cibleBrute.Trim();

                if (sous.Length == 0 || cible.Length == 0) continue;

                // Le champ tolère les deux formes : « net » comme
                // « net.mcemonitor.org ». Sans cela, un nom complet saisi par
                // l utilisateur donnait net.mcemonitor.org.mcemonitor.org.
                string hote = sous == domaine || sous.EndsWith("." + domaine, StringComparison.Ordinal)
                    ? sous
                    : $"{sous}.{domaine}";

                // Deux sites du même nom font échouer Caddy, qui refuse alors son
                // fichier entier : on garde le premier, et Conflit() prévient l'UI.
                if (sites.Any(s => string.Equals(s.Hote, hote, StringComparison.OrdinalIgnoreCase)))
                    continue;

                sites.Add(new Site(hote, cible));
            }

            return sites;
        }

        /// <summary>
        /// Premier nom réclamé deux fois, ou null. Sert à refuser l'enregistrement
        /// avant d'écrire un Caddyfile que Caddy rejetterait en bloc.
        /// </summary>
        public static string? Conflit(HttpsConfig config)
        {
            string domaine = config.Domaine.Trim().TrimEnd('.').ToLowerInvariant();
            if (domaine.Length == 0) return null;

            var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (sousDomaine, cible) in Candidats(config))
            {
                string sous = sousDomaine.Trim().Trim('.', ' ').ToLowerInvariant();
                if (sous.Length == 0 || cible.Trim().Length == 0) continue;

                string hote = sous == domaine || sous.EndsWith("." + domaine, StringComparison.Ordinal)
                    ? sous
                    : $"{sous}.{domaine}";

                if (!vus.Add(hote)) return hote;
            }

            return null;
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

            // Redirection HTTP vers HTTPS, un bloc par site. « auto_https off »
            // désactive l automatisme de Caddy, y compris cette redirection : sans ces
            // blocs, une adresse tapée en http:// s afficherait en clair.
            foreach (var site in sites)
            {
                lignes.Add($"http://{site.Hote} {{");
                lignes.Add($"\tredir https://{site.Hote}{{uri}} permanent");
                lignes.Add("}");
                lignes.Add("");
            }

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
