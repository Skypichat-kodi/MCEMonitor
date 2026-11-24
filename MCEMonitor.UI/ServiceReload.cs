using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MCEMonitor.UI
{
    /// <summary>
    /// Demande à un service de recharger sa configuration à chaud, par le même
    /// canal IPC que son interface dédiée.
    ///
    /// Sans cela, un réglage enregistré depuis MCEMonitor était bien écrit dans le
    /// fichier, mais le service continuait de tourner avec les anciennes valeurs
    /// jusqu'à son redémarrage — alors que l'enregistrement depuis l'interface du
    /// module était appliqué immédiatement.
    /// </summary>
    internal static class ServiceReload
    {
        public const string PipeRom    = "MCEMonitor_RomMonitorPipe";
        public const string PipeSystem = "MCEMonitor_SystemMonitorPipe";
        public const string PipeNet    = "MCEMonitor_NetMonitorPipe";

        // Le service répond « {"status":"ok"} » une fois le rechargement terminé.
        // Certains rechargements relisent la blacklist et les plages IP : les trois
        // secondes d'origine expiraient avant la réponse, d'où un message d'échec
        // alors que le service était bien vivant et le rechargement bien appliqué.
        private const int DelaiConnexionMs = 2000;
        private const int DelaiReponseMs   = 12000;

        /// <summary>
        /// Envoie « reload-config » et retourne vrai si le service a répondu autre
        /// chose qu'une erreur. Toute panne du canal se traduit par un faux, jamais
        /// par une exception : l'enregistrement du fichier, lui, a déjà réussi.
        /// </summary>
        public static async Task<bool> RechargerAsync(string nomPipe)
        {
            var chrono = Stopwatch.StartNew();

            try
            {
                using var client = new NamedPipeClientStream(
                    ".", nomPipe, PipeDirection.InOut, PipeOptions.Asynchronous);

                try
                {
                    using var connexion = new CancellationTokenSource(DelaiConnexionMs);
                    await client.ConnectAsync(connexion.Token);
                }
                catch (OperationCanceledException)
                {
                    // Le service n'expose aucune instance du canal : il est arrêté,
                    // ou pas encore prêt. Ce n'est pas un silence de sa part.
                    CoreLog.Warn("Service", $"{nomPipe} : canal injoignable après {DelaiConnexionMs} ms (service arrêté ?)");
                    return false;
                }

                if (!client.IsConnected)
                {
                    CoreLog.Warn("Service", $"{nomPipe} : connexion impossible après {DelaiConnexionMs} ms");
                    return false;
                }

                client.ReadMode = PipeTransmissionMode.Byte;

                // Même protocole que les clients des modules : les octets bruts,
                // sans marqueur de fin de ligne. Le service lit puis applique Trim.
                byte[] commande = Encoding.UTF8.GetBytes("reload-config");
                await client.WriteAsync(commande, 0, commande.Length);
                await client.FlushAsync();

                using var delai = new CancellationTokenSource(DelaiReponseMs);

                byte[] tampon  = new byte[4096];
                var reponse    = new StringBuilder();

                while (true)
                {
                    int lus = await client.ReadAsync(tampon, 0, tampon.Length, delai.Token);
                    if (lus <= 0)
                        break;

                    reponse.Append(Encoding.UTF8.GetString(tampon, 0, lus));
                }

                chrono.Stop();
                string texte = reponse.ToString().Trim();

                if (texte.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    CoreLog.Warn("Service", $"{nomPipe} : réponse d'erreur « {texte} »");
                    return false;
                }

                CoreLog.Info("Service", $"{nomPipe} : rechargement confirmé en {chrono.ElapsedMilliseconds} ms");

                return texte.Length > 0;
            }
            catch (OperationCanceledException)
            {
                CoreLog.Warn("Service", $"{nomPipe} : aucune réponse après {DelaiReponseMs} ms");
                return false;
            }
            catch (Exception ex)
            {
                CoreLog.Warn("Service", $"{nomPipe} : {ex.GetType().Name} — {ex.Message}");
                return false;
            }
        }
    }
}
