using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MediaMonitor.Core.Services;

namespace MediaMonitor.Core.DvbViewer
{
    public class DvbViewerStatusClient
    {
        private readonly HttpClient _http;

        public string BaseUrl { get; }
        public string Username { get; }
        public string Password { get; }

        public DvbViewerStatusClient(string baseUrl, string username, string password)
        {
            BaseUrl = baseUrl?.TrimEnd('/') ?? "";
            Username = username;
            Password = password;

            // Handler avec authentification Basic
            var handler = new HttpClientHandler
            {
                Credentials = new System.Net.NetworkCredential(username, password)
            };

            _http = new HttpClient(handler);
        }

        public async Task<List<DvbViewerClientStream>> GetClientStreamsAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(BaseUrl))
                    return new List<DvbViewerClientStream>();

                string url = BaseUrl;

                // Si l’UI n’a pas mis ?aktion=status, on le rajoute
                if (!url.Contains("status.html"))
                    url = url.TrimEnd('/') + "/status.html?aktion=status";

                string html = await _http.GetStringAsync(url);

                return DvbViewerStatusParser.Parse(html);
            }
            catch (Exception ex)
            {
                CoreLog.Write("DVBViewer ERROR: " + ex.Message);
                return new List<DvbViewerClientStream>();
            }
        }
    }
}

