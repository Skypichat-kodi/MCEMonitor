using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace MCEMonitor.UI.Partage
{
    /// <summary>
    /// Démarre, arrête et surveille la passerelle Caddy.
    ///
    /// Caddy est lancé par « caddy start », qui se détache et rend la main : le
    /// processus survit à MCEMonitor, et la tâche planifiée peut le relancer au
    /// démarrage de la machine.
    /// </summary>
    public static class GestionnairePasserelle
    {
        /// <summary>Adresse de l'API d'administration, fixée dans le Caddyfile.</summary>
        private const string ApiAdmin = "http://127.0.0.1:2019/config/";

        /// <summary>
        /// La passerelle répond-elle ? On interroge l'API d'administration plutôt
        /// que la liste des processus : un Caddy lancé à la main, sans notre
        /// fichier, ne compte pas.
        /// </summary>
        public static bool EstEnMarche()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var reponse = client.GetAsync(ApiAdmin).GetAwaiter().GetResult();
                return reponse.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Écrit le Caddyfile puis démarre Caddy. Renvoie le message de Caddy, ou
        /// une chaîne vide en cas de succès silencieux.
        /// </summary>
        public static async Task<string> DemarrerAsync()
        {
            if (!TelechargeurCaddy.EstInstalle)
                throw new InvalidOperationException("Caddy n'est pas installé.");

            var config = HttpsConfig.Charger();

            if (!GenerateurCaddyfile.Ecrire(config))
                throw new InvalidOperationException(
                    "Aucun sous-domaine n'est renseigné : il n'y a rien à publier.");

            if (EstEnMarche())
            {
                await RechargerAsync();
                return "";
            }

            string sortie = await ExecuterAsync("start", "--config", GenerateurCaddyfile.CheminCaddyfile);

            CoreLog.Info("HTTPS", "Passerelle démarrée." + (sortie.Length > 0 ? " " + sortie : ""));
            return sortie;
        }

        /// <summary>Arrête la passerelle en douceur, par son API d'administration.</summary>
        public static async Task ArreterAsync()
        {
            if (!EstEnMarche()) return;

            await ExecuterAsync("stop");

            CoreLog.Info("HTTPS", "Passerelle arrêtée.");
        }

        /// <summary>
        /// Recharge la configuration sans interrompre les connexions en cours.
        /// C'est ce qui rend un certificat renouvelé immédiatement actif : Caddy ne
        /// relit pas un certificat fichier de lui-même.
        /// </summary>
        public static async Task RechargerAsync()
        {
            if (!EstEnMarche()) return;

            string sortie = await ExecuterAsync("reload", "--config", GenerateurCaddyfile.CheminCaddyfile);

            if (sortie.Length > 0)
                CoreLog.Warn("HTTPS", "Rechargement de la passerelle : " + sortie);
            else
                CoreLog.Info("HTTPS", "Passerelle rechargée (certificat à jour).");
        }

        private static async Task<string> ExecuterAsync(params string[] arguments)
        {
            var psi = new ProcessStartInfo(TelechargeurCaddy.CheminCaddy)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments) psi.ArgumentList.Add(argument);

            using var processus = Process.Start(psi)
                ?? throw new InvalidOperationException("Caddy n'a pas pu démarrer.");

            // caddy start attend que l'API réponde : si le port 443 est occupé, ou
            // si les droits manquent, il peut attendre indéfiniment. Sans ce délai,
            // l'interface resterait figée.
            using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var lectureSortie = processus.StandardOutput.ReadToEndAsync();
            var lectureErreurs = processus.StandardError.ReadToEndAsync();

            try
            {
                await processus.WaitForExitAsync(delai.Token);
            }
            catch (OperationCanceledException)
            {
                try { processus.Kill(entireProcessTree: true); } catch { }

                throw new InvalidOperationException(
                    "Caddy n'a pas démarré dans les trente secondes. Le port 443 est peut-être occupé, "
                    + "ou les droits administrateur manquent.");
            }

            string sortie = lectureSortie.Result;
            string erreurs = lectureErreurs.Result;

            if (processus.ExitCode != 0)
                throw new InvalidOperationException((erreurs + " " + sortie).Trim());

            return sortie.Trim();
        }
    }
}
