using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MCEMonitorClient.Tray.Ipc;
using MCEMonitorClient.Tray.Logs;
using MCEMonitorClient.Tray.Models;
using MCEMonitorClient.Tray.Services;
using System.Runtime.InteropServices;

namespace MCEMonitorClient.Tray
{
    public class TrayApplicationContext : ApplicationContext
    {
        // --- État ---
        private string _globalState = "ok";
        private readonly PushListener _pushListener = new();
        private System.Windows.Forms.Timer? _pollTimer;

        // ? MODIFIÉ : map ServerId ? status ("ok"|"warning"|"critical"|"offline")
        //            pour recalculer l'icône dans le bon ordre de priorité
        private readonly Dictionary<string, string> _serverStatuses = new();
        private readonly object _statusLock = new object();

        // --- Icônes ---
        private Icon _iconOk = null!;        // connected.png
        private Icon _iconWarning = null!;   // warning.png
        private Icon _iconCritical = null!;  // critical.png
        private Icon _iconOffline = null!;   // icône app (fallback)

        // --- Tray ---
        private NotifyIcon _trayIcon = null!;
        private bool _initialStateNotified = false;

        // --- Sons ---
        private System.Media.SoundPlayer? _alarmPlayer;
        private System.Media.SoundPlayer? _warningPlayer;

        // --- Sons système Windows (par alias) ---
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

        private const uint SND_ALIAS     = 0x00010000;
        private const uint SND_ASYNC     = 0x0001;
        private const uint SND_NODEFAULT = 0x0002;

        private static void PlaySystemSound(string alias)
        {
            try { PlaySound(alias, IntPtr.Zero, SND_ALIAS | SND_ASYNC); } catch { }
        }

        // ---------------------------------------------
        //  Constructeur
        // ---------------------------------------------
        public TrayApplicationContext()
        {
            CoreLog.Clear();
            CoreLog.Write("=== Tray démarré ===");

            ToastHelper.Initialize();

            LoadIcons();
            LoadSounds();
            InitializeTray();
            InitializePushListener();
            StartPolling();
        }

        // ---------------------------------------------
        //  Icônes  ? MODIFIÉ
        // ---------------------------------------------
        private void LoadIcons()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
            string iconsDir = Path.Combine(exeDir, "Resources", "Icons");

            // Icône app par défaut (utilisée pour l'état offline + fallback de chargement)
            string appIco = Path.Combine(exeDir, "MCEMonitorClient.ico");
            var defaultIcon = File.Exists(appIco) ? new Icon(appIco) : SystemIcons.Application;

            // Ordre de priorité : critical.png > warning.png > connected.png > icône app
            _iconOk       = LoadIconFromPng(Path.Combine(iconsDir, "connected.png")) ?? defaultIcon;
            _iconWarning  = LoadIconFromPng(Path.Combine(iconsDir, "warning.png"))   ?? defaultIcon;
            _iconCritical = LoadIconFromPng(Path.Combine(iconsDir, "critical.png"))  ?? defaultIcon;
            _iconOffline  = defaultIcon; // offline = icône app (pas de PNG dédié)
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static Icon? LoadIconFromPng(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    CoreLog.Write($"[ICON] Fichier introuvable : {path}");
                    return null;
                }

                using var original = new Bitmap(path);

                const int size = 16;
                using var resized = new Bitmap(size, size);
                using (var g = Graphics.FromImage(resized))
                {
                    g.SmoothingMode     = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode   = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(original, 0, 0, size, size);
                }

                IntPtr hIcon = resized.GetHicon();
                try
                {
                    using var tmp = Icon.FromHandle(hIcon);
                    Icon clone = (Icon)tmp.Clone();
                    CoreLog.Write($"[ICON] OK : {Path.GetFileName(path)} " +
                                  $"({original.Width}×{original.Height} ? {size}×{size})");
                    return clone;
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write($"[ICON] Erreur chargement '{path}' : {ex.Message}");
                return null;
            }
        }

        private void LoadSounds()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
            string soundsDir = Path.Combine(exeDir, "Resources", "Sounds");

            string alarmPath = Path.Combine(soundsDir, "alarm.wav");
            if (File.Exists(alarmPath)) _alarmPlayer = new System.Media.SoundPlayer(alarmPath);

            string warningPath = Path.Combine(soundsDir, "warning.wav");
            if (File.Exists(warningPath)) _warningPlayer = new System.Media.SoundPlayer(warningPath);
        }

        // ---------------------------------------------
        //  Tray
        // ---------------------------------------------
        private void InitializeTray()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = _iconOk,
                Visible = true,
                Text = "MCEMonitorClient"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Ouvrir la configuration", null, (s, e) => OpenConfig());
            menu.Items.Add("Voir les serveurs", null, (s, e) => ShowServersPopup());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Rafraîchir maintenant", null, async (s, e) => await ForceScanAsync());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quitter", null, (s, e) => Exit());

            _trayIcon.ContextMenuStrip = menu;

            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) OpenConfig();
            };
        }

        // ---------------------------------------------
        //  Push listener
        // ---------------------------------------------
        private void InitializePushListener()
        {
            _pushListener.OnAlert += OnAlertReceived;
            _pushListener.OnMedia += OnMediaReceived;
            _pushListener.Start();
        }

        // ? MODIFIÉ : met à jour la map + recalcule l'icône (au lieu d'utiliser
        //             aveuglément alert.Status, ce qui écrasait un état plus prioritaire)
        private void OnAlertReceived(PushAlert alert)
        {
            SetServerStatus(alert.ServerId, alert.Status);
            UpdateTrayIcon();
            ShowNotification(alert);
        }

        // ---------------------------------------------
        //  ? NOUVEAU : gestion priorité des icônes
        // ---------------------------------------------
        private void SetServerStatus(string serverId, string status)
        {
            if (string.IsNullOrEmpty(serverId)) return;
            lock (_statusLock) _serverStatuses[serverId] = string.IsNullOrEmpty(status) ? "ok" : status;
        }

        /// <summary>
        /// Calcule l'état global en respectant l'ordre de priorité :
        /// critical > warning > tous online > offline (fallback icône app).
        /// </summary>
        private string ComputeWorstState()
        {
            lock (_statusLock)
            {
                if (_serverStatuses.Count == 0) return "ok";

                // P1 : Critique
                if (_serverStatuses.Values.Any(s => s == "critical")) return "critical";

                // P2 : Warning
                if (_serverStatuses.Values.Any(s => s == "warning")) return "warning";

                // P3 : Tous online (aucun offline)
                if (!_serverStatuses.Values.Any(s => s == "offline")) return "ok";

                // P4 : Fallback (au moins un serveur offline)
                return "offline";
            }
        }

        /// <summary>
        /// Applique l'icône et le tooltip en fonction de l'état global recalculé.
        /// </summary>
        private void UpdateTrayIcon()
        {
            string worst = ComputeWorstState();
            _globalState = worst;

            var icon = worst switch
            {
                "critical" => _iconCritical,
                "warning"  => _iconWarning,
                "ok"       => _iconOk,
                _          => _iconOffline
            };

            _trayIcon.Icon = icon;

            int total, offline;
            lock (_statusLock)
            {
                total = _serverStatuses.Count;
                offline = _serverStatuses.Values.Count(s => s == "offline");
            }
            int online = total - offline;

            _trayIcon.Text = worst switch
            {
                "critical" => $"MCEMonitorClient - CRITIQUE ({online}/{total} en ligne)",
                "warning"  => $"MCEMonitorClient - Alerte ({online}/{total} en ligne)",
                "offline"  => $"MCEMonitorClient - Hors ligne ({online}/{total} en ligne)",
                _          => $"MCEMonitorClient - OK ({online}/{total} en ligne)"
            };
        }

        // ---------------------------------------------
        //  Notifications (Toast)
        // ---------------------------------------------
        private void ShowNotification(PushAlert alert)
        {
            try
            {
                string title;
                string message;
                bool isCritical = false;
                bool isWarning  = false;
                bool isOffline  = false;

                if (alert.Status == "ok" && alert.PreviousStatus != "ok")
                {
                    title = $"{alert.ServerName} - Retour à la normale";
                    message = "Le serveur est de nouveau joignable";
                }
                else if (alert.Status == "offline")
                {
                    title = $"{alert.ServerName} - Hors ligne";
                    message = "Le serveur ne répond plus";
                    isOffline = true;
                }
                else if (alert.Problems != null && alert.Problems.Count > 0)
                {
                    int count = alert.Problems.Count;
                    title = count == 1
                        ? $"{alert.ServerName} - 1 problème détecté"
                        : $"{alert.ServerName} - {count} problèmes détectés";

                    var lines = new List<string>();
                    int maxShown = 3;
                    for (int i = 0; i < Math.Min(count, maxShown); i++)
                    {
                        var p = alert.Problems[i];
                        string prefix = p.Severity == "critical" ? "[CRIT]" : "[WARN]";
                        lines.Add($"{prefix} {p.Message}");
                    }
                    if (count > maxShown) lines.Add($"... et {count - maxShown} autre(s)");
                    message = string.Join("\n", lines);

                    if (alert.Status == "critical")     isCritical = true;
                    else if (alert.Status == "warning") isWarning  = true;
                }
                else
                {
                    title = $"{alert.ServerName} - Changement d'état";
                    message = string.IsNullOrEmpty(alert.FirstProblem)
                        ? $"État : {alert.Status}"
                        : alert.FirstProblem;
                }

                string iconFile = GetAlertIcon(alert);
                bool silent = isCritical || isWarning || isOffline;

                ToastHelper.Show(title, message, iconFile, alert.BaseUrl, silent);

                if (isCritical)     PlayAlarm();
                else if (isWarning) PlayWarning();
                else if (isOffline) PlaySystemSound("DeviceDisconnect");
            }
            catch (Exception ex)
            {
                CoreLog.Write("ShowNotification ERROR : " + ex.Message);
            }
        }

        private void PlayAlarm()
        {
            Task.Run(() =>
            {
                try
                {
                    if (_alarmPlayer != null) _alarmPlayer.PlaySync();
                    else Console.Beep(2200, 250);
                }
                catch { }
            });
        }

        private void PlayWarning()
        {
            Task.Run(() =>
            {
                try
                {
                    if (_warningPlayer != null) _warningPlayer.PlaySync();
                    else Console.Beep(1200, 200);
                }
                catch { }
            });
        }

        // ---------------------------------------------
        //  Polling (fallback : pull toutes les 30s)
        // ---------------------------------------------
        private void StartPolling()
        {
            _pollTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            _pollTimer.Tick += async (s, e) => await RefreshFromServiceAsync();
            _pollTimer.Start();

            _ = RefreshFromServiceAsync();
        }

        // ? MODIFIÉ : reconstruit la map des statuts puis recalcule l'icône
        private async Task RefreshFromServiceAsync()
        {
            try
            {
                var state = await ServiceIpcClient.GetStateAsync();
                if (state == null) return;

                // Reconstruit la map ServerId ? status (source de vérité)
                var newStatuses = new Dictionary<string, string>();
                foreach (var s in state.Servers)
                {
                    string st = !s.Online
                        ? "offline"
                        : (string.IsNullOrEmpty(s.Status) ? "ok" : s.Status);
                    newStatuses[s.ServerId] = st;
                }

                lock (_statusLock)
                {
                    _serverStatuses.Clear();
                    foreach (var kv in newStatuses) _serverStatuses[kv.Key] = kv.Value;
                }

                // Applique l'icône via la logique de priorité
                UpdateTrayIcon();

                // Première notif au démarrage (inchangé)
                if (!_initialStateNotified)
                {
                    _initialStateNotified = true;

                    foreach (var server in state.Servers)
                    {
                        if (server.Status != "ok" && server.Online)
                        {
                            CoreLog.Write($"[INIT] Problème au démarrage : {server.ServerName} = {server.Status}");

                            var alert = new PushAlert
                            {
                                Type = "alert",
                                ServerId = server.ServerId,
                                ServerName = server.ServerName,
                                ServiceType = server.ServiceType,
                                BaseUrl = server.BaseUrl,
                                Status = server.Status,
                                PreviousStatus = "unknown",
                                ProblemCount = server.ProblemCount,
                                FirstProblem = server.FirstProblem,
                                Problems = server.Problems?
                                    .Select(p => new Ipc.ProblemItem
                                    {
                                        Severity = p.Severity,
                                        Category = p.Category,
                                        Message  = p.Message
                                    })
                                    .ToList() ?? new List<Ipc.ProblemItem>(),
                                Timestamp = server.Timestamp
                            };

                            ShowNotification(alert);
                        }
                        else if (!server.Online)
                        {
                            CoreLog.Write($"[INIT] Serveur hors ligne au démarrage : {server.ServerName}");

                            var alert = new PushAlert
                            {
                                Type = "alert",
                                ServerId = server.ServerId,
                                ServerName = server.ServerName,
                                ServiceType = server.ServiceType,
                                BaseUrl = server.BaseUrl,
                                Status = "offline",
                                PreviousStatus = "unknown",
                                ProblemCount = 0,
                                FirstProblem = "Le serveur ne répond plus",
                                Problems = new(),
                                Timestamp = server.Timestamp
                            };

                            ShowNotification(alert);
                        }
                    }
                }
            }
            catch { }
        }

        private async Task ForceScanAsync()
        {
            try
            {
                await ServiceIpcClient.ForceScanAsync();
                await RefreshFromServiceAsync();
            }
            catch { }
        }

        // ---------------------------------------------
        //  Actions
        // ---------------------------------------------
        private void OpenConfig()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
                string configExe = Path.Combine(exeDir, "MCEMonitorClient.Config.exe");

                if (File.Exists(configExe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = configExe,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show("MCEMonitorClient.Config.exe est introuvable.",
                        "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir la configuration : " + ex.Message);
            }
        }

        private void ShowServersPopup()
        {
            try
            {
                var form = new ServersPopupForm();
                form.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message);
            }
        }

        private void Exit()
        {
            try
            {
                CoreLog.Write("Tray en cours d'arrêt...");

                _pushListener.Stop();
                _pollTimer?.Stop();

                try
                {
                    using var client = new System.IO.Pipes.NamedPipeClientStream(
                        ".", "MCEMonitor_ClientPipe", System.IO.Pipes.PipeDirection.InOut);
                    client.Connect(1000);
                    using var writer = new System.IO.StreamWriter(client);
                    writer.WriteLine("shutdown");
                    writer.Flush();
                    System.Threading.Thread.Sleep(800);
                }
                catch { }

                for (int i = 0; i < 50; i++)
                {
                    if (Process.GetProcessesByName("MCEMonitorClient.Service").Length == 0) break;
                    System.Threading.Thread.Sleep(100);
                }

                foreach (var p in Process.GetProcessesByName("MCEMonitorClient.Service"))
                {
                    try { p.Kill(); } catch { }
                }

                _trayIcon.Visible = false;
                _trayIcon.Dispose();

                CoreLog.Write("Tray quitté");
                Application.Exit();
            }
            catch (Exception ex)
            {
                CoreLog.Write("Exit ERROR : " + ex.Message);
                try
                {
                    foreach (var p in Process.GetProcessesByName("MCEMonitorClient.Service"))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }
                Application.Exit();
            }
        }

        // ---------------------------------------------
        //  Notifications média
        // ---------------------------------------------
        private void OnMediaReceived(PushMedia media)
        {
            CoreLog.Write($"OnMediaReceived : {media.EventType} '{media.Title}' sur {media.ServerName}");

            try
            {
                if (media.EventType == "stopped" && IsAudioType(media.MediaType))
                {
                    CoreLog.Write("OnMediaReceived : arrêt audio ignoré");
                    return;
                }

                string iconFile = GetMediaIcon(media.MediaType, media.EventType);

                if (media.EventType == "started" || media.EventType == "stopped")
                {
                    string title = media.EventType == "started"
                        ? $"{media.ServerName} - Lecture en cours"
                        : $"{media.ServerName} - Lecture terminée";

                    string info = "";
                    if (!string.IsNullOrEmpty(media.Client)) info += $"Depuis : {media.Client}\n";
                    info += string.IsNullOrEmpty(media.Title) ? media.MediaType : media.Title;
                    if (media.Saison > 0 || media.Episode > 0)
                        info += $"  ({media.Saison:00}x{media.Episode:00})";

                    ToastHelper.Show(title, info, iconFile, media.BaseUrl);
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("OnMediaReceived ERROR : " + ex.Message);
            }
        }

        private static string GetMediaIcon(string? mediaType, string eventType)
        {
            string t = (mediaType ?? "").Trim().ToLowerInvariant();

            // --- Cas particulier : enregistrement ? icône fixe "dot-red.png" ---
            // (indépendant de started/stopped : c'est le point rouge d'enregistrement)
            if (t == "rec" || t == "recording" || t == "enregistrement")
                return "dot-red.png";

            bool isStop = eventType == "stopped";
            string prefix = isStop ? "stop-" : "play-";
            string fallback = isStop ? "stop.png" : "play.png";

            string suffix = t switch
            {
                "audio"  or "music" or "musique"            => "audio",
                "video"  or "movie" or "film"               => "video",
                "serie"  or "series" or "tv" or "episode"   => "tv",
                "image"  or "photo" or "picture"            => "image",
                _                                            => ""
            };

            return string.IsNullOrEmpty(suffix) ? fallback : prefix + suffix + ".png";
        }

        private static bool IsAudioType(string? mediaType)
        {
            string t = (mediaType ?? "").Trim().ToLowerInvariant();
            return t == "audio" || t == "music" || t == "musique";
        }

        private static string GetAlertIcon(PushAlert alert)
        {
            if (alert.Problems != null && alert.Problems.Count > 0)
            {
                // 1) Critique disque
                foreach (var p in alert.Problems)
                    if (p.Severity == "critical" && IsDiskProblem(p)) return "hdd-critical.png";

                // 2) Critique CPU température
                foreach (var p in alert.Problems)
                    if (p.Severity == "critical" && IsCpuTempProblem(p)) return "cpu-temp.png";

                // 3) Critique CPU surcharge
                foreach (var p in alert.Problems)
                    if (p.Severity == "critical" && IsCpuProblem(p)) return "cpu-overload.png";

                // 4) Warning disque
                foreach (var p in alert.Problems)
                    if (p.Severity == "warning" && IsDiskProblem(p)) return "hdd-warning.png";
            }

            return alert.Status switch
            {
                "critical" => "critical.png",
                "warning"  => "warning.png",
                "offline"  => "disconnected.png",
                _          => "connected.png"
            };
        }

        private static bool IsDiskProblem(Ipc.ProblemItem p)
        {
            string cat = (p.Category ?? "").ToLowerInvariant();
            string msg = (p.Message ?? "").ToLowerInvariant();
            return cat.Contains("disk") || cat.Contains("smart") || cat.Contains("hdd")
                || cat.Contains("ssd")  || cat.Contains("storage")
                || msg.Contains("disk") || msg.Contains("disque") || msg.Contains("smart") || msg.Contains("hdd");
        }

        private static bool IsCpuTempProblem(Ipc.ProblemItem p)
        {
            string cat = (p.Category ?? "").ToLowerInvariant();
            string msg = (p.Message ?? "").ToLowerInvariant();
            return (cat.Contains("cpu") && (cat.Contains("temp") || cat.Contains("thermal")))
                || (msg.Contains("cpu") && (msg.Contains("temp") || msg.Contains("chaud") || msg.Contains("°c")))
                || cat.Contains("temperature") || cat.Contains("thermal");
        }

        private static bool IsCpuProblem(Ipc.ProblemItem p)
        {
            string cat = (p.Category ?? "").ToLowerInvariant();
            string msg = (p.Message ?? "").ToLowerInvariant();
            return cat.Contains("cpu") || cat.Contains("processor")
                || msg.Contains("cpu") || msg.Contains("processeur");
        }
    }
}