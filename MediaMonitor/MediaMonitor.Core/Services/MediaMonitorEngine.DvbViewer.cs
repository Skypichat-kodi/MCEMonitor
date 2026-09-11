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
        public async Task<(string BaseUrl, List<DvbViewerClientStream> Streams)> GetDvbViewerStreamsAsync()
        {
            CoreLog.Write("DVB: GetDvbViewerStreamsAsync() appelé");

            if (!DvbViewerEnabled)
            {
                CoreLog.Write("DVB: Fonction DVBViewer RS désactivée ? aucun appel effectué.");
                return ("", new List<DvbViewerClientStream>());
            }

            try
            {
                if (string.IsNullOrWhiteSpace(DvbViewerUrl))
                {
                    CoreLog.Write("DVB: URL non configurée.");
                    return ("", new List<DvbViewerClientStream>());
                }

                CoreLog.Write($"DVB: URL brute = '{DvbViewerUrl}'");
                CoreLog.Write($"DVB: User = '{DvbViewerUser}'");

                string baseUrl = DvbViewerUrl.Trim();

                if (!baseUrl.Contains("status.html"))
                {
                    if (!baseUrl.EndsWith("/"))
                        baseUrl += "/";

                    baseUrl += "status.html?aktion=status";
                }

                CoreLog.Write($"DVB: URL finale = '{baseUrl}'");

                var client = new DvbViewerStatusClient(
                    baseUrl,
                    DvbViewerUser,
                    DvbViewerPass
                );

                CoreLog.Write("DVB: Appel client.GetClientStreamsAsync()...");
                var streams = await client.GetClientStreamsAsync();

                CoreLog.Write($"DVB: {streams.Count} flux trouvés");

                foreach (var s in streams)
                    CoreLog.Write($"DVB: Flux => {s.Client} | {s.Type} | {s.Nom}");

                // ? IMPORTANT : on renvoie l’URL que NOUS avons construite
                return (baseUrl, streams);
            }
            catch (Exception ex)
            {
                CoreLog.Write("DVB ERROR: " + ex.ToString());
                return ("", new List<DvbViewerClientStream>());
            }
        }
    }
}

