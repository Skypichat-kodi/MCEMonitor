using System;
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

        private const int TimeoutMs = 3000;

        /// <summary>
        /// Envoie « reload-config » et retourne vrai si le service a répondu autre
        /// chose qu'une erreur. Toute panne du canal se traduit par un faux, jamais
        /// par une exception : l'enregistrement du fichier, lui, a déjà réussi.
        /// </summary>
        public static async Task<bool> RechargerAsync(string nomPipe)
        {
            try
            {
                using var delai = new CancellationTokenSource(TimeoutMs);

                using var client = new NamedPipeClientStream(
                    ".", nomPipe, PipeDirection.InOut, PipeOptions.Asynchronous);

                await client.ConnectAsync(delai.Token);

                using var writer = new StreamWriter(client, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
                using var reader = new StreamReader(client, Encoding.UTF8);

                await writer.WriteLineAsync("reload-config");

                string? reponse = await reader.ReadLineAsync(delai.Token);

                return reponse != null
                    && reponse.IndexOf("error", StringComparison.OrdinalIgnoreCase) < 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
