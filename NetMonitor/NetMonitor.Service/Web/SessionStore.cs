using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using System.Text.Json;

namespace NetMonitor.Service.Web
{
    /// <summary>
    /// Sessions de login unifiées (auth + OTP).
    /// Créées après un POST /login réussi, valables 15 min par défaut.
    /// </summary>
    public static class SessionStore
    {
        private static readonly ConcurrentDictionary<string, DateTime> _sessions = new();

        // ═══════════════════════════════════════════════════════════
        //  Appareils de confiance (cookie "Se souvenir de moi")
        // ═══════════════════════════════════════════════════════════
        public class TrustedDevice
        {
            public string Fingerprint { get; set; } = "";
            public string UserAgent { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public DateTime ExpiresAt { get; set; }
        }

        private static readonly ConcurrentDictionary<string, TrustedDevice> _trustedDevices = new();
        private const int TrustedDeviceDays = 30;
        private static readonly string _hmacSecret = LoadOrCreateHmacSecret();

        // ═══════════════════════════════════════════════════════════
        //  Persistance des appareils de confiance
        //  Fichier : %CommonAppData%\MCEMonitor\NetMonitor\trusted_devices.json
        //  Objectif : survivre à un arrêt / redémarrage du service
        // ═══════════════════════════════════════════════════════════
        private static readonly string _trustedStorePath = BuildTrustedStorePath();
        private static readonly object _trustedFileLock = new();
        private static bool _trustedLoaded;
        private static readonly JsonSerializerOptions _trustedJsonOptions = new() { WriteIndented = true };

        public static string CreateTrustedDevice(string userAgent)
        {
            EnsureTrustedLoaded();

            string fingerprint = ComputeDeviceFingerprint(userAgent);
            var now = DateTime.UtcNow;

            _trustedDevices[fingerprint] = new TrustedDevice
            {
                Fingerprint = fingerprint,
                UserAgent   = userAgent ?? "",
                CreatedAt   = now,
                ExpiresAt   = now.AddDays(TrustedDeviceDays)
            };

            CoreLog.Info($"[TRUSTED] Nouvel appareil de confiance enregistré (expire dans {TrustedDeviceDays}j)");
            SaveTrustedDevices();
            return fingerprint;
        }

        public static bool IsTrustedDevice(string? fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint)) return false;

            EnsureTrustedLoaded();

            if (!_trustedDevices.TryGetValue(fingerprint, out var device)) return false;

            if (device.ExpiresAt < DateTime.UtcNow)
            {
                _trustedDevices.TryRemove(fingerprint, out _);
                SaveTrustedDevices();
                return false;
            }
            return true;
        }

        public static void RevokeTrustedDevice(string? fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint)) return;

            EnsureTrustedLoaded();

            if (_trustedDevices.TryRemove(fingerprint, out var removed))
            {
                CoreLog.Info($"[TRUSTED] Appareil révoqué : {TruncateUA(removed.UserAgent)}");
                SaveTrustedDevices();
            }
        }

        public static void RevokeAllTrustedDevices()
        {
            EnsureTrustedLoaded();

            int count = _trustedDevices.Count;
            _trustedDevices.Clear();
            SaveTrustedDevices();
            CoreLog.Info($"[TRUSTED] {count} appareil(s) de confiance révoqué(s)");
        }

        public static int TrustedDeviceCount
        {
            get
            {
                EnsureTrustedLoaded();
                return _trustedDevices.Count;
            }
        }

        public static List<TrustedDevice> GetAllTrustedDevices()
        {
            EnsureTrustedLoaded();

            // Purge les expirés avant de renvoyer
            var now = DateTime.UtcNow;
            var expired = _trustedDevices.Where(kv => kv.Value.ExpiresAt < now)
                                         .Select(kv => kv.Key)
                                         .ToList();
            foreach (var key in expired)
                _trustedDevices.TryRemove(key, out _);

            if (expired.Count > 0)
                SaveTrustedDevices();

            return _trustedDevices.Values
                .OrderByDescending(d => d.CreatedAt)
                .ToList();
        }

        /// <summary>
        /// Charge la liste des appareils de confiance depuis le disque (une seule fois).
        /// Les entrées expirées sont ignorées.
        /// </summary>
        private static void EnsureTrustedLoaded()
        {
            lock (_trustedFileLock)
            {
                if (_trustedLoaded) return;

                // Posé avant la lecture : une erreur de disque ne doit pas réessayer à chaque requête
                _trustedLoaded = true;

                try
                {
                    if (!File.Exists(_trustedStorePath)) return;

                    string json = File.ReadAllText(_trustedStorePath);
                    var devices = JsonSerializer.Deserialize<List<TrustedDevice>>(json, _trustedJsonOptions);
                    if (devices == null) return;

                    var now = DateTime.UtcNow;
                    int loaded = 0;

                    foreach (var d in devices)
                    {
                        if (string.IsNullOrWhiteSpace(d.Fingerprint)) continue;
                        if (d.ExpiresAt < now) continue;

                        _trustedDevices[d.Fingerprint] = d;
                        loaded++;
                    }

                    CoreLog.Info($"[TRUSTED] {loaded} appareil(s) de confiance rechargé(s) depuis le disque");
                }
                catch (Exception ex)
                {
                    CoreLog.Error($"[TRUSTED] Lecture de {_trustedStorePath} impossible : {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Écrit la liste des appareils de confiance sur le disque (écriture atomique).
        /// </summary>
        private static void SaveTrustedDevices()
        {
            lock (_trustedFileLock)
            {
                try
                {
                    string? folder = Path.GetDirectoryName(_trustedStorePath);
                    if (!string.IsNullOrEmpty(folder))
                        Directory.CreateDirectory(folder);

                    var list = _trustedDevices.Values
                        .OrderBy(d => d.CreatedAt)
                        .ToList();

                    string json = JsonSerializer.Serialize(list, _trustedJsonOptions);

                    // Fichier temporaire puis remplacement : évite un JSON tronqué si coupure
                    string tmp = _trustedStorePath + ".tmp";
                    File.WriteAllText(tmp, json);
                    File.Move(tmp, _trustedStorePath, overwrite: true);
                }
                catch (Exception ex)
                {
                    CoreLog.Error($"[TRUSTED] Écriture de {_trustedStorePath} impossible : {ex.Message}");
                }
            }
        }

        private static string BuildTrustedStorePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MCEMonitor", "NetMonitor", "trusted_devices.json");
        }

        /// <summary>
        /// Sérialise la liste des appareils de confiance en JSON (pour IPC).
        /// </summary>
        public static string GetTrustedDevicesJson()
        {
            var devices = GetAllTrustedDevices().Select(d => new
            {
                fingerprint = d.Fingerprint,
                userAgent   = d.UserAgent,
                createdAt   = d.CreatedAt,
                expiresAt   = d.ExpiresAt
            }).ToList();

            return System.Text.Json.JsonSerializer.Serialize(devices,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
        }

        private static string TruncateUA(string? ua)
        {
            if (string.IsNullOrEmpty(ua)) return "(unknown)";
            return ua.Length <= 60 ? ua : ua.Substring(0, 57) + "…";
        }

        public static string Create(int minutes)
        {
            string token = Guid.NewGuid().ToString("N");
            _sessions[token] = DateTime.UtcNow.AddMinutes(minutes);
            return token;
        }

        public static bool IsValid(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            if (!_sessions.TryGetValue(token, out var expires)) return false;

            if (expires < DateTime.UtcNow)
            {
                _sessions.TryRemove(token, out _);
                return false;
            }
            return true;
        }

        public static void Invalidate(string? token)
        {
            if (!string.IsNullOrWhiteSpace(token))
                _sessions.TryRemove(token, out _);
        }

        // ═══════════════════════════════════════════════════════════
        //  Helpers HMAC (signature des empreintes d'appareil)
        // ═══════════════════════════════════════════════════════════

        private static string ComputeDeviceFingerprint(string userAgent)
        {
            string raw = (userAgent ?? "") + "|" + _hmacSecret;
            byte[] hash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 32);
        }

        /// <summary>
        /// Charge (ou crée) le sel HMAC utilisé pour signer les empreintes.
        /// Persisté dans %CommonAppData%\MCEMonitor\NetMonitor\hmac_secret.key
        /// </summary>
        private static string LoadOrCreateHmacSecret()
        {
            try
            {
                string folder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor", "NetMonitor");
                string path = System.IO.Path.Combine(folder, "hmac_secret.key");

                if (System.IO.File.Exists(path))
                    return System.IO.File.ReadAllText(path).Trim();

                System.IO.Directory.CreateDirectory(folder);
                string secret = Convert.ToHexString(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                System.IO.File.WriteAllText(path, secret);
                return secret;
            }
            catch
            {
                // Fallback : secret éphémère (les cookies ne survivront pas au redémarrage)
                return Convert.ToHexString(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            }
        }
    }
}