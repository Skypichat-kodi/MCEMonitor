using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MCEMonitor.UI.Partage
{
    /// <summary>État du certificat installé, tel que l'affiche l'onglet.</summary>
    public sealed class EtatCertificat
    {
        public bool   Present     { get; init; }
        public string Sujet       { get; init; } = "";
        public string Emetteur    { get; init; } = "";
        public DateTime ExpireLe  { get; init; }

        public int JoursRestants => Present ? (int)(ExpireLe - DateTime.Now).TotalDays : 0;

        public static readonly EtatCertificat Absent = new();
    }

    /// <summary>
    /// Obtient et renouvelle le certificat TLS par le défi DNS-01.
    ///
    /// Aucun port n'est ouvert : la preuve de propriété est un enregistrement TXT
    /// déposé dans la zone du domaine, que lego écrit puis efface. Cela permet
    /// d'obtenir le certificat avant même que les enregistrements A existent.
    /// </summary>
    public static class GestionnaireCertificat
    {
        /// <summary>Racine de travail de lego : comptes ACME et certificats.</summary>
        public static readonly string DossierCerts = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "Certs");

        public static string DossierFichiers => Path.Combine(DossierCerts, "certificates");

        /// <summary>
        /// Fichier du certificat pour ce domaine. lego nomme le fichier d'après le
        /// premier domaine demandé ; un wildcard verrait son astérisque remplacé par
        /// un souligné, d'où le repli sur ce nom.
        /// </summary>
        public static string CheminCertificat(string domaine)
        {
            string normal = domaine.Trim().ToLowerInvariant();
            string direct = Path.Combine(DossierFichiers, normal + ".crt");

            if (File.Exists(direct)) return direct;

            string wildcard = Path.Combine(DossierFichiers, "_." + normal + ".crt");
            return File.Exists(wildcard) ? wildcard : direct;
        }

        /// <summary>
        /// Lance lego en défi DNS-01. Les identifiants passent par l'environnement du
        /// processus, jamais par la ligne de commande où ils seraient visibles dans la
        /// liste des processus.
        /// </summary>
        /// <param name="etapes">Messages d'avancement, affichés dans la fenêtre.</param>
        public static async Task ObtenirAsync(HttpsConfig config, IProgress<string> etapes, CancellationToken jeton)
        {
            if (!TelechargeurLego.EstInstalle)
                throw new InvalidOperationException("lego n'est pas installé.");

            string domaine = config.Domaine.Trim().ToLowerInvariant();
            if (domaine.Length == 0)
                throw new InvalidOperationException("Aucun nom de domaine n'est renseigné.");

            var fournisseur = FournisseursDns.ParId(config.Fournisseur);
            var manquants = new List<string>();

            foreach (var champ in fournisseur.Champs)
            {
                bool renseigne = config.Identifiants.TryGetValue(champ.Cle, out var valeur) &&
                                 !string.IsNullOrWhiteSpace(valeur);
                if (!renseigne && string.IsNullOrEmpty(champ.Defaut))
                    manquants.Add(champ.Cle);
            }

            if (manquants.Count > 0)
                throw new InvalidOperationException("Identifiants incomplets : " + string.Join(", ", manquants));

            Directory.CreateDirectory(DossierCerts);

            // --- Ligne de commande ---
            // En lego 5, toutes ces options appartiennent à la commande « run » :
            // --dns, --path, --email et -d la suivent obligatoirement.
            var arguments = new List<string> { "run", "--accept-tos" };

            if (!string.IsNullOrWhiteSpace(config.Email))
            {
                arguments.Add("--email");
                arguments.Add(config.Email.Trim());
            }

            arguments.Add("--dns");
            arguments.Add(fournisseur.Id);
            arguments.Add("--path");
            arguments.Add(DossierCerts);
            arguments.Add("-d");
            arguments.Add(domaine);
            arguments.Add("-d");
            arguments.Add("*." + domaine);

            // --- Environnement : les variables attendues par le fournisseur ---
            var environnement = new Dictionary<string, string>();

            foreach (var champ in fournisseur.Champs)
            {
                string valeur = config.Identifiants.TryGetValue(champ.Cle, out var v) && !string.IsNullOrWhiteSpace(v)
                    ? v
                    : champ.Defaut;

                if (!string.IsNullOrEmpty(valeur)) environnement[champ.Cle] = valeur;
            }

            etapes.Report($"Demande du certificat pour {domaine} (défi DNS chez {fournisseur.Nom})…");

            var sortie = await ExecuterAsync(TelechargeurLego.CheminLego, arguments, environnement, jeton);

            // --- Contrôle du résultat ---
            string fichier = CheminCertificat(domaine);

            if (!File.Exists(fichier))
            {
                CoreLog.Error("HTTPS", "lego n'a produit aucun certificat. Sortie : " + sortie);
                throw new InvalidOperationException(
                    "Aucun certificat produit. Dernière ligne de lego : " + DerniereLigne(sortie));
            }

            etapes.Report("Certificat obtenu.");
            CoreLog.Info("HTTPS", $"Certificat obtenu pour {domaine} : {fichier}");
        }

        private static async Task<string> ExecuterAsync(string executable,
                                                        List<string> arguments,
                                                        Dictionary<string, string> environnement,
                                                        CancellationToken jeton)
        {
            var psi = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var argument in arguments) psi.ArgumentList.Add(argument);
            foreach (var (cle, valeur) in environnement) psi.Environment[cle] = valeur;

            using var processus = Process.Start(psi)
                ?? throw new InvalidOperationException("lego n'a pas pu démarrer.");

            var sortie = new StringBuilder();

            processus.OutputDataReceived += (_, e) => { if (e.Data != null) sortie.AppendLine(e.Data); };
            processus.ErrorDataReceived  += (_, e) => { if (e.Data != null) sortie.AppendLine(e.Data); };

            processus.BeginOutputReadLine();
            processus.BeginErrorReadLine();

            // L'attente DNS peut prendre une minute ; on surveille l'annulation.
            while (!processus.HasExited)
            {
                if (jeton.IsCancellationRequested)
                {
                    try { processus.Kill(entireProcessTree: true); } catch { }
                    jeton.ThrowIfCancellationRequested();
                }

                await Task.Delay(300, CancellationToken.None);
            }

            return sortie.ToString().Trim();
        }

        private static string DerniereLigne(string sortie)
        {
            var lignes = sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            for (int i = lignes.Length - 1; i >= 0; i--)
            {
                string ligne = lignes[i].Trim();
                if (ligne.Length > 0) return ligne;
            }

            return "(aucune sortie)";
        }

        /// <summary>Lit le certificat installé, ou renvoie Absent.</summary>
        public static EtatCertificat Lire(string domaine)
        {
            try
            {
                string fichier = CheminCertificat(domaine);
                if (!File.Exists(fichier)) return EtatCertificat.Absent;

                using var certificat = new X509Certificate2(fichier);

                return new EtatCertificat
                {
                    Present    = true,
                    Sujet      = certificat.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
                    Emetteur   = certificat.GetNameInfo(X509NameType.SimpleName, forIssuer: true),
                    ExpireLe   = certificat.NotAfter
                };
            }
            catch (Exception ex)
            {
                CoreLog.Error("HTTPS", "Lecture du certificat : " + ex.Message);
                return EtatCertificat.Absent;
            }
        }
    }
}
