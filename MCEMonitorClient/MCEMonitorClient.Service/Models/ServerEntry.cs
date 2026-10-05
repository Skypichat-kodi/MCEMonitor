using System;
using System.Text.Json.Serialization;

namespace MCEMonitorClient.Service.Models
{
    public class ServerEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string BaseUrl { get; set; } = "";  // ex: "http://192.168.1.19" (SANS port)
        public int Port { get; set; } = 8083;      // ? NOUVEAU
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string ServiceType { get; set; } = "";
        public bool Enabled { get; set; } = true;

        /// <summary>URL complète avec port (calculée).</summary>
        [JsonIgnore]
        public string FullUrl =>
            string.IsNullOrWhiteSpace(BaseUrl)
                ? ""
                : $"{BaseUrl.TrimEnd('/')}:{Port}";

        /// <summary>URL du endpoint /api/summary (calculée).</summary>
        [JsonIgnore]
        public string ApiSummaryUrl =>
            string.IsNullOrWhiteSpace(FullUrl) ? "" : $"{FullUrl}/api/summary";
    }
}