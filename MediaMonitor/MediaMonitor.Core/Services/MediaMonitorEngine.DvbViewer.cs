using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediaMonitor.Core.DvbViewer;

namespace MediaMonitor.Core.Services
{
    public partial class MediaMonitorEngine
    {
        /// <summary>
        /// Retourne la liste des streams DVBViewer :
        /// - 1 ligne par enregistrement en cours
        /// - 1 ligne par client LiveTV
        /// </summary>
        public async Task<List<DvbViewerClientStream>> GetDvbViewerStreamsAsync()
        {
            CoreLog.Write("DVB: GetDvbViewerStreamsAsync() appelé");

            try
            {
                // Vérification configuration
                if (string.IsNullOrWhiteSpace(DvbViewerUrl))
                {
                    CoreLog.Write("DVB: URL non configurée.");
                    return new List<DvbViewerClientStream>();
                }

                CoreLog.Write($"DVB: URL brute = '{DvbViewerUrl}'");
                CoreLog.Write($"DVB: User = '{DvbViewerUser}'");

                // Nettoyage URL
                string baseUrl = DvbViewerUrl.Trim();

                if (!baseUrl.Contains("status.html"))
                {
                    if (!baseUrl.EndsWith("/"))
                        baseUrl += "/";

                    baseUrl += "status.html?aktion=status";
                }

                CoreLog.Write($"DVB: URL finale = '{baseUrl}'");

                // Création client
                var client = new DvbViewerStatusClient(
                    baseUrl,
                    DvbViewerUser,
                    DvbViewerPass
                );

                CoreLog.Write("DVB: Appel client.GetClientStreamsAsync()...");

                // Appel HTTP + parsing
                var streams = await client.GetClientStreamsAsync();

                CoreLog.Write($"DVB: {streams.Count} flux trouvés");

                foreach (var s in streams)
                    CoreLog.Write($"DVB: Flux => {s.Client} | {s.Type} | {s.Nom}");

                return streams;
            }
            catch (Exception ex)
            {
                CoreLog.Write("DVB ERROR: " + ex.ToString());
                return new List<DvbViewerClientStream>();
            }
        }
    }
}

