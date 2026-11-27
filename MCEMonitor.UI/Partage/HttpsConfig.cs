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

        public string Domaine        { get; set; } = "";

        // Sous-domaines, un par module. Vide = non publié.
        public string SousNetMonitor    { get; set; } = "";
        public string SousRomMonitor    { get; set; } = "";
        public string SousSystemMonitor { get; set; } = "";
        public string SousMediaMonitor  { get; set; } = "";

        public string Fournisseur { get; set; } = "Infomaniak";
        public string Email       { get; set; } = "";
        public int    Port        { get; set; } = 443;

        /// <summary>Jeton d'API en clair. Jamais écrit tel quel sur disque.</summary>
        public string Jeton { get; set; } = "";

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
                        case "Domaine":                 config.Domaine = valeur; break;
                        case "SousNetMonitor":          config.SousNetMonitor = valeur; break;
                        case "SousRomMonitor":          config.SousRomMonitor = valeur; break;
                        case "SousSystemMonitor":       config.SousSystemMonitor = valeur; break;
                        case "SousMediaMonitor":        config.SousMediaMonitor = valeur; break;
                        case "Fournisseur":             config.Fournisseur = valeur; break;
                        case "Email":                   config.Email = valeur; break;
                        case "Port":                    if (int.TryParse(valeur, out int p)) config.Port = p; break;
                        case "Jeton":                   config.Jeton = ProtectionHttps.Dechiffrer(valeur) ?? ""; break;
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
                    "",
                    "# --- Domaine ---",
                    $"Domaine={Domaine}",
                    $"SousNetMonitor={SousNetMonitor}",
                    $"SousRomMonitor={SousRomMonitor}",
                    $"SousSystemMonitor={SousSystemMonitor}",
                    $"SousMediaMonitor={SousMediaMonitor}",
                    "",
                    "# --- Certificat ---",
                    $"Fournisseur={Fournisseur}",
                    $"Email={Email}",
                    "# Jeton chiffre (DPAPI machine), jamais en clair",
                    $"Jeton={ProtectionHttps.Chiffrer(Jeton)}",
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
    }

    /// <summary>
    /// Chiffrement du jeton DNS. Entropie propre au module : les autres
    /// fichiers de la suite ne doivent pas pouvoir le déchiffrer, et
    /// réciproquement.
    /// </summary>
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
