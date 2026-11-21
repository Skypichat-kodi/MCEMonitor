using System;
using System.Text.Json.Serialization;

namespace MCEMonitorClient.Config.Models
{
    public class ServerEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string BaseUrl { get; set; } = "";  // ex: "http://192.168.1.19" (SANS port)
        public int Port { get; set; } = 8083;      // ? NOUVEAU
        public string Username { get; set; } = "";

        /// <summary>Mot de passe en mémoire : n'est jamais sérialisé (voir PasswordEnc).</summary>
        [JsonIgnore]
        public string Password { get; set; } = "";

        /// <summary>Ancienne forme en clair, lue pour migration puis effacée à la première sauvegarde.</summary>
        [JsonPropertyName("password")]
        public string PasswordLegacy { get; set; } = "";

        /// <summary>Mot de passe chiffré DPAPI (base64) : seule forme écrite sur disque.</summary>
        public string PasswordEnc { get; set; } = "";

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