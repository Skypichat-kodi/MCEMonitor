using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace NetMonitor.Service // <-- DOIT ETRE NetMonitor.Service
{
    public class EmailConfig
    {
        public string Server { get; set; } = "";
        public int Port { get; set; } = 465;
        public string From { get; set; } = "";
        public string Password { get; set; } = "";
        public string To { get; set; } = "";
        public string SecurityMode { get; set; } = "SSL";

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "email.config"
        );

        public static EmailConfig Load()
        {
            var cfg = new EmailConfig();
            try
            {
                if (!File.Exists(ConfigPath)) return cfg;

                // Deux formes possibles pour le mot de passe :
                //   PasswordEnc = valeur chiffrée DPAPI (forme courante)
                //   Password    = ancienne forme en clair (compatibilité)
                string motDePasseClair   = "";
                string motDePasseChiffre = "";

                foreach (var line in File.ReadAllLines(ConfigPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains('=')) continue;
                    var parts = line.Split('=', 2);
                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    switch (key)
                    {
                        case "Server": cfg.Server = value; break;
                        case "Port": if (int.TryParse(value, out int p)) cfg.Port = p; break;
                        case "From": cfg.From = value; break;
                        case "Password":    motDePasseClair   = value; break;
                        case "PasswordEnc": motDePasseChiffre = value; break;
                        case "To": cfg.To = value; break;
                        case "SecurityMode": cfg.SecurityMode = value; break;
                    }
                }

                // La forme chiffrée prime : si elle est présente, l'ancienne valeur
                // en clair est ignorée (elle n'est plus réécrite par l'interface).
                cfg.Password = !string.IsNullOrWhiteSpace(motDePasseChiffre)
                    ? (ProtectionEmail.Dechiffrer(motDePasseChiffre) ?? "")
                    : motDePasseClair;
            }
            catch (Exception ex) { CoreLog.Write("Erreur Load EmailConfig : " + ex.Message); }
            return cfg;
        }
    }

    /// <summary>
    /// Chiffrement du mot de passe SMTP par DPAPI.
    ///
    /// Portée LocalMachine : le service (SYSTEM) et l'interface (administrateur)
    /// doivent pouvoir déchiffrer la même valeur, alors que ce sont deux comptes
    /// différents — CurrentUser rendrait la valeur illisible pour l'un des deux.
    ///
    /// IMPORTANT : l'entropie doit être IDENTIQUE dans tous les modules, puisque
    /// email.config leur est commun. Une entropie différente rendrait le mot de
    /// passe indéchiffrable d'un module à l'autre.
    /// </summary>
    internal static class ProtectionEmail
    {
        private static readonly byte[] Entropie = Encoding.UTF8.GetBytes("MCEMonitor.email.v1");

        public static string Chiffrer(string clair)
        {
            if (string.IsNullOrEmpty(clair)) return string.Empty;

            try
            {
                byte[] octets   = Encoding.UTF8.GetBytes(clair);
                byte[] proteges = ProtectedData.Protect(octets, Entropie, DataProtectionScope.LocalMachine);
                return Convert.ToBase64String(proteges);
            }
            catch (Exception ex)
            {
                CoreLog.Write("Chiffrement du mot de passe SMTP impossible : " + ex.Message);
                return string.Empty;
            }
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
            catch
            {
                // Fichier venu d'un autre poste, ou écrit avec une autre entropie.
                return null;
            }
        }
    }
}