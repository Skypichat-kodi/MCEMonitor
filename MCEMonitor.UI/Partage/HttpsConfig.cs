using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MCEMonitor.UI.Partage
{
    /// <summary>
    /// Configuration du module « HTTPS » de MCEMonitor : accès distant et
    /// certificat. Fichier propre, à côté des autres réglages de la suite.
    ///
    /// Le module démarre inerte : rien n'est lancé, aucun port n'est ouvert.
    /// Cette configuration ne fait que conserver des intentions, jusqu'à ce que
    /// la passerelle existe.
    /// </summary>
    public class HttpsConfig
    {
        public static readonly string CheminFichier = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor", "acces-distant.config");

        /// <summary>Accès distant activé.</summary>
        public bool Actif { get; set; }

        /// <summary>Mise à jour automatique de l enregistrement A.</summary>
        public bool Ddns { get; set; }

        public string Domaine        { get; set; } = "";

        // Sous-domaines, un par module. Vide = non publié.
        public string SousNetMonitor    { get; set; } = "";
        public string SousRomMonitor    { get; set; } = "";
        public string SousSystemMonitor { get; set; } = "";
        public string SousMediaMonitor  { get; set; } = "";

        // Cibles de la passerelle : où joindre chaque service. 127.0.0.1 quand le
        // module tourne sur la même machine que la passerelle, sinon son adresse
        // sur le réseau local. Les ports sont ceux par défaut de chaque module.
        public string CibleNetMonitor    { get; set; } = "127.0.0.1:14003";
        public string CibleRomMonitor    { get; set; } = "127.0.0.1:8085";
        public string CibleSystemMonitor { get; set; } = "127.0.0.1:8083";
        public string CibleMediaMonitor  { get; set; } = "127.0.0.1:8081";

        public string Fournisseur { get; set; } = "infomaniak";
        public string Email       { get; set; } = "";
        public int    Port        { get; set; } = 443;

        /// <summary>
        /// Valeurs saisies pour les champs du fournisseur, indexées par nom de
        /// variable d'environnement (CLOUDFLARE_DNS_API_TOKEN, OVH_APPLICATION_KEY…).
        /// Elles sont secrètes : chiffrées en bloc sur disque.
        /// </summary>
        public Dictionary<string, string> Identifiants { get; set; } = new();

        // ============================================================
        //  Lecture / écriture
        // ============================================================
        public static HttpsConfig Charger()
        {
            var config = new HttpsConfig();

            try
            {
                if (!File.Exists(CheminFichier)) return config;

                foreach (string ligne in File.ReadAllLines(CheminFichier))
                {
                    string l = ligne.Trim();
                    if (l.Length == 0 || l.StartsWith("#")) continue;

                    int egal = l.IndexOf('=');
                    if (egal <= 0) continue;

                    string cle = l.Substring(0, egal).Trim();
                    string valeur = l.Substring(egal + 1).Trim();

                    switch (cle)
                    {
                        case "Actif":                   config.Actif = bool.TryParse(valeur, out bool a) && a; break;
                        case "Ddns":                    config.Ddns = bool.TryParse(valeur, out bool d) && d; break;
                        case "Domaine":                 config.Domaine = valeur; break;
                        case "SousNetMonitor":          config.SousNetMonitor = valeur; break;
                        case "SousRomMonitor":          config.SousRomMonitor = valeur; break;
                        case "SousSystemMonitor":       config.SousSystemMonitor = valeur; break;
                        case "SousMediaMonitor":        config.SousMediaMonitor = valeur; break;
                        case "CibleNetMonitor":         config.CibleNetMonitor = valeur; break;
                        case "CibleRomMonitor":         config.CibleRomMonitor = valeur; break;
                        case "CibleSystemMonitor":      config.CibleSystemMonitor = valeur; break;
                        case "CibleMediaMonitor":       config.CibleMediaMonitor = valeur; break;
                        case "Fournisseur":             config.Fournisseur = valeur; break;
                        case "Email":                   config.Email = valeur; break;
                        case "Port":                    if (int.TryParse(valeur, out int p)) config.Port = p; break;
                        case "Identifiants":            config.Identifiants = LireIdentifiants(valeur); break;
                    }
                }
            }
            catch (Exception ex)
            {
                CoreLog.Error("HTTPS", $"HttpsConfig.Charger : {ex.Message}");
            }

            return config;
        }

        public void Enregistrer()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CheminFichier)!);

                var lignes = new List<string>
                {
                    "# Acces distant (HTTPS) - module inerte tant que la passerelle n existe pas",
                    "",
                    "# Actif=false : les modules restent servis en HTTP, rien n est lance",
                    $"Actif={Actif.ToString().ToLower()}",
                    $"Ddns={Ddns.ToString().ToLower()}",
                    "",
                    "# --- Domaine ---",
                    $"Domaine={Domaine}",
                    $"SousNetMonitor={SousNetMonitor}",
                    $"SousRomMonitor={SousRomMonitor}",
                    $"SousSystemMonitor={SousSystemMonitor}",
                    $"SousMediaMonitor={SousMediaMonitor}",
                    "",
                    "# --- Cibles de la passerelle (hote:port) ---",
                    $"CibleNetMonitor={CibleNetMonitor}",
                    $"CibleRomMonitor={CibleRomMonitor}",
                    $"CibleSystemMonitor={CibleSystemMonitor}",
                    $"CibleMediaMonitor={CibleMediaMonitor}",
                    "",
                    "# --- Certificat ---",
                    $"Fournisseur={Fournisseur}",
                    $"Email={Email}",
                    "# Identifiants chiffres (DPAPI machine) : un objet JSON par fournisseur",
                    $"Identifiants={EcrireIdentifiants()}",
                    "",
                    "# --- Passerelle ---",
                    $"Port={Port}",
                    ""
                };

                File.WriteAllLines(CheminFichier, lignes);
            }
            catch (Exception ex)
            {
                CoreLog.Error("HTTPS", $"HttpsConfig.Enregistrer : {ex.Message}");
            }
        }
        // ============================================================
        //  Identifiants : un dictionnaire, chiffré en bloc
        // ============================================================
        private static readonly System.Text.Json.JsonSerializerOptions OptionsJson = new()
        {
            WriteIndented = false
        };

        private string EcrireIdentifiants()
        {
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(Identifiants, OptionsJson);
                return ProtectionHttps.Chiffrer(json);
            }
            catch (Exception ex)
            {
                CoreLog.Error("HTTPS", $"HttpsConfig.EcrireIdentifiants : {ex.Message}");
                return string.Empty;
            }
        }

        private static Dictionary<string, string> LireIdentifiants(string chiffre)
        {
            var vide = new Dictionary<string, string>();

            try
            {
                string? json = ProtectionHttps.Dechiffrer(chiffre);
                if (string.IsNullOrWhiteSpace(json)) return vide;

                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? vide;
            }
            catch (Exception ex)
            {
                CoreLog.Error("HTTPS", $"HttpsConfig.LireIdentifiants : {ex.Message}");
                return vide;
            }
        }
    }
    internal static class ProtectionHttps
    {
        private static readonly byte[] Entropie = Encoding.UTF8.GetBytes("MCEMonitor.accesdistant.v1");

        public static string Chiffrer(string clair)
        {
            if (string.IsNullOrEmpty(clair)) return string.Empty;

            try
            {
                byte[] octets   = Encoding.UTF8.GetBytes(clair);
                byte[] proteges = ProtectedData.Protect(octets, Entropie, DataProtectionScope.LocalMachine);
                return Convert.ToBase64String(proteges);
            }
            catch { return string.Empty; }
        }

        /// <summary>Renvoie null si la valeur est vide ou indéchiffrable sur ce poste.</summary>
        public static string? Dechiffrer(string chiffre)
        {
            if (string.IsNullOrWhiteSpace(chiffre)) return null;

            try
            {
                byte[] proteges = Convert.FromBase64String(chiffre);
                byte[] octets   = ProtectedData.Unprotect(proteges, Entropie, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(octets);
            }
            catch { return null; }
        }
    }
}
