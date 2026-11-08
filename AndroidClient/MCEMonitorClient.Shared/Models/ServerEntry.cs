using System;
using System.Text.Json.Serialization;

namespace MCEMonitorClient.Shared.Models
{
    public class ServerEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public int Port { get; set; } = 8083;

        // Identifiants stockés dans SecureStorage (Android), jamais dans le JSON
        [JsonIgnore]
        public string Username { get; set; } = "";

        [JsonIgnore]
        public string Password { get; set; } = "";

        public string ServiceType { get; set; } = "";
        public bool Enabled { get; set; } = true;

        [JsonIgnore]
        public string FullUrl =>
            string.IsNullOrWhiteSpace(BaseUrl)
                ? ""
                : $"{BaseUrl.TrimEnd('/')}:{Port}";

        [JsonIgnore]
        public string ApiSummaryUrl =>
            string.IsNullOrWhiteSpace(FullUrl) ? "" : $"{FullUrl}/api/summary";
    }
}