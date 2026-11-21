using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

public class EmailConfig
{
    public string From { get; set; } = "";
    public string Password { get; set; } = "";
    public string To { get; set; } = "";
    public string Server { get; set; } = "";
    public int Port { get; set; } = 465;
    public string SecurityMode { get; set; } = "SSL";

    private static string GetConfigPath()
    {
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor"
        );

        Directory.CreateDirectory(baseDir);

        return Path.Combine(baseDir, "email.config");
    }

    public static EmailConfig Load() => LoadFromUserFolder();
    public void Save() => SaveToUserFolder();

    public static EmailConfig LoadFromUserFolder()
    {
        string path = GetConfigPath();
        var cfg = new EmailConfig();

        if (!File.Exists(path))
            return cfg;

        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            var parts = line.Split('=', 2);
            if (parts.Length != 2)
                continue;

            string key = parts[0].Trim().ToLowerInvariant();
            string value = parts[1].Trim();

            switch (key)
            {
                case "from": cfg.From = value; break;
                case "password": cfg.Password = value; break;
                case "passwordenc": cfg.Password = ProtectionEmail.Dechiffrer(value) ?? ""; break;
                case "to": cfg.To = value; break;
                case "server": cfg.Server = value; break;
                case "port": if (int.TryParse(value, out int p)) cfg.Port = p; break;
                case "securitymode": cfg.SecurityMode = value; break;
            }
        }

        return cfg;
    }

    public void SaveToUserFolder()
    {
        string path = GetConfigPath();
        using var sw = new StreamWriter(path, false);

        sw.WriteLine("From=" + From);
        sw.WriteLine("PasswordEnc=" + ProtectionEmail.Chiffrer(Password));
        sw.WriteLine("To=" + To);
        sw.WriteLine("Server=" + Server);
        sw.WriteLine("Port=" + Port);
        sw.WriteLine("SecurityMode=" + SecurityMode);
    }

    /// <summary>
    /// Chiffrement du mot de passe SMTP par DPAPI (portée LocalMachine : services en
    /// SYSTEM et interface en administrateur doivent lire la même valeur).
    /// L'entropie doit être IDENTIQUE dans tous les modules : email.config est commun.
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
            catch
            {
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
                return null;
            }
        }
    }
}

