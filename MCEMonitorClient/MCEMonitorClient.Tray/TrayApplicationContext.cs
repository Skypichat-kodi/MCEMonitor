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

namespace MCEMonitorClient.Tray
{
    public class TrayApplicationContext : ApplicationContext
    {
        // --- État ---
        private string _globalState = "ok";
        private readonly PushListener _pushListener = new();
        private System.Windows.Forms.Timer? _pollTimer;

        // --- Icônes ---
        private Icon _iconOk = null!;
        private Icon _iconWarning = null!;
        private Icon _iconCritical = null!;
        private Icon _iconOffline = null!;

        // --- Tray ---
        private NotifyIcon _trayIcon = null!;
        private bool _initialStateNotified = false;

        // --- Sons ---
        private System.Media.SoundPlayer? _alarmPlayer;
        private System.Media.SoundPlayer? _warningPlayer;

        // ---------------------------------------------
        //  Constructeur (UN SEUL)
        // ---------------------------------------------
        public TrayApplicationContext()
        {
            CoreLog.Clear();
            CoreLog.Write("=== Tray démarré ===");

            // ? Enregistre l'app pour les notifications modernes
            ToastHelper.Initialize();

            LoadIcons();
            LoadSounds();
            InitializeTray();
            InitializePushListener();
            StartPolling();
        }

        // ---------------------------------------------
        //  Icônes
        // ---------------------------------------------
        private void LoadIcons()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";

            string ico = Path.Combine(exeDir, "MCEMonitorClient.ico");
            var defaultIcon = File.Exists(ico) ? new Icon(ico) : SystemIcons.Application;

            string iconsDir = Path.Combine(exeDir, "Resources", "Icons");

            _iconOk       = LoadIconFromPng(Path.Combine(iconsDir, "dot-green.png"))  ?? defaultIcon;
            _iconWarning  = LoadIconFromPng(Path.Combine(iconsDir, "dot-yellow.png")) ?? defaultIcon;
            _iconCritical = LoadIconFromPng(Path.Combine(iconsDir, "dot-red.png"))    ?? defaultIcon;
            _iconOffline  = LoadIconFromPng(Path.Combine(iconsDir, "dot-gray.png"))   ?? defaultIcon;
        }

        private static Icon? LoadIconFromPng(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                using var bmp = new Bitmap(path);
                IntPtr hIcon = bmp.GetHicon();
                using var tmp = Icon.FromHandle(hIcon);
                return (Icon)tmp.Clone();
            }
            catch { return null; }
        }

        private void LoadSounds()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
            string soundsDir = Path.Combine(exeDir, "Resources", "Sounds");

            string alarmPath = Path.Combine(soundsDir, "alarm.wav");
            if (File.Exists(alarmPath))
                _alarmPlayer = new System.Media.SoundPlayer(alarmPath);

            string warningPath = Path.Combine(soundsDir, "warning.wav");
            if (File.Exists(warningPath))
                _warningPlayer = new System.Media.SoundPlayer(warningPath);
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
            _trayIcon.DoubleClick += (s, e) => ShowServersPopup();
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

        private void OnAlertReceived(PushAlert alert)
        {
            UpdateIconFromAlert(alert);
            ShowNotification(alert);
        }

        private void UpdateIconFromAlert(PushAlert alert)
        {
            var icon = alert.Status switch
            {
                "critical" => _iconCritical,
                "warning"  => _iconWarning,
                "offline"  => _iconOffline,
                _          => _iconOk
            };

            _trayIcon.Icon = icon;

            _trayIcon.Text = alert.Status switch
            {
                "critical" => $"MCEMonitorClient - CRITIQUE : {alert.ServerName}",
                "warning"  => $"MCEMonitorClient - Alerte : {alert.ServerName}",
                "offline"  => $"MCEMonitorClient - Hors ligne : {alert.ServerName}",
                _          => $"MCEMonitorClient - OK : {alert.ServerName}"
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

                // --- Cas 1 : retour à la normale ---
                if (alert.Status == "ok" && alert.PreviousStatus != "ok")
                {
                    title = $"{alert.ServerName} - Retour à la normale";
                    message = "Le serveur est de nouveau joignable";
                }
                // --- Cas 2 : serveur hors ligne ---
                else if (alert.Status == "offline")
                {
                    title = $"{alert.ServerName} - Hors ligne";
                    message = "Le serveur ne répond plus";
                    isCritical = true;
                }
                // --- Cas 3 : problèmes détectés ---
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

                        // ? Marqueur ASCII (compatible Toast)
                        string prefix = p.Severity == "critical" ? "[CRIT]" : "[WARN]";
                        lines.Add($"{prefix} {p.Message}");
                    }

                    if (count > maxShown)
                        lines.Add($"... et {count - maxShown} autre(s)");

                    message = string.Join("\n", lines);

                    if (alert.Status == "critical")
                        isCritical = true;
                }
                // --- Cas 4 : changement générique ---
                else
                {
                    title = $"{alert.ServerName} - Changement d'état";
                    message = string.IsNullOrEmpty(alert.FirstProblem)
                        ? $"État : {alert.Status}"
                        : alert.FirstProblem;
                }

                // Choisit l'icône selon l'état
                string iconFile = alert.Status switch
                {
                    "critical" => "dot-red.png",
                    "warning"  => "dot-yellow.png",
                    "offline"  => "dot-gray.png",
                    _          => "dot-green.png"
                };

                ToastHelper.Show(title, message, iconFile, alert.BaseUrl);

                if (isCritical)
                    PlayAlarm();
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
                    if (_alarmPlayer != null)
                        _alarmPlayer.PlaySync();
                    else
                        Console.Beep(2200, 250);
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

        private async Task RefreshFromServiceAsync()
        {
            try
            {
                var state = await ServiceIpcClient.GetStateAsync();
                if (state == null) return;

                _globalState = state.GlobalState;

                // Met à jour l'icône selon l'état global
                var icon = state.GlobalState switch
                {
                    "critical" => _iconCritical,
                    "warning"  => _iconWarning,
                    "offline"  => _iconOffline,
                    _          => _iconOk
                };

                _trayIcon.Icon = icon;

                int online = state.Servers.Count(s => s.Online);
                int total = state.Servers.Count;

                _trayIcon.Text = state.GlobalState switch
                {
                    "critical" => $"MCEMonitorClient - CRITIQUE ({online}/{total} en ligne)",
                    "warning"  => $"MCEMonitorClient - Alerte ({online}/{total} en ligne)",
                    "offline"  => $"MCEMonitorClient - Hors ligne ({online}/{total} en ligne)",
                    _          => $"MCEMonitorClient - OK ({online}/{total} en ligne)"
                };

                // ? Première fois qu'on récupère l'état ? on notifie les problèmes actuels
                if (!_initialStateNotified)
                {
                    _initialStateNotified = true;

                    foreach (var server in state.Servers)
                    {
                        if (server.Status != "ok" && server.Online)
                        {
                            CoreLog.Write($"[INIT] Problème au démarrage : {server.ServerName} = {server.Status}");

                            // Convertit PollResult en PushAlert pour réutiliser ShowNotification()
                            var alert = new PushAlert
                            {
                                Type = "alert",
                                ServerId = server.ServerId,
                                ServerName = server.ServerName,
                                ServiceType = server.ServiceType,
                                BaseUrl = server.BaseUrl,
                                Status = server.Status,
                                PreviousStatus = "unknown",   // pas "ok" ? ShowNotification va notifier
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

                // 1. Arrête le listener push
                _pushListener.Stop();
                _pollTimer?.Stop();

                // 2. Envoie "shutdown" au Service via IPC
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

                // 3. Attend que le Service s'arrête (max 5s)
                for (int i = 0; i < 50; i++)
                {
                    if (Process.GetProcessesByName("MCEMonitorClient.Service").Length == 0)
                        break;

                    System.Threading.Thread.Sleep(100);
                }

                // 4. Si le Service tourne encore ? kill
                foreach (var p in Process.GetProcessesByName("MCEMonitorClient.Service"))
                {
                    try { p.Kill(); } catch { }
                }

                // 5. Ferme le Tray
                _trayIcon.Visible = false;
                _trayIcon.Dispose();

                CoreLog.Write("Tray quitté");

                Application.Exit();
            }
            catch (Exception ex)
            {
                CoreLog.Write("Exit ERROR : " + ex.Message);

                // Fallback : on force la sortie
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
                if (media.EventType == "started")
                {
                    string title = $"{media.ServerName} - Lecture en cours";
                    string info = string.IsNullOrEmpty(media.Title) ? media.MediaType : media.Title;

                    if (media.Saison > 0 || media.Episode > 0)
                        info += $"  ({media.Saison:00}x{media.Episode:00})";

                    if (!string.IsNullOrEmpty(media.Client))
                        info += $"\nClient : {media.Client}";

                    ToastHelper.Show(title, info, "play.png", media.BaseUrl);
                }
                else if (media.EventType == "stopped")
                {
                    string title = $"{media.ServerName} - Lecture terminée";
                    string info = string.IsNullOrEmpty(media.Title) ? media.MediaType : media.Title;

                    if (media.Saison > 0 || media.Episode > 0)
                        info += $"  ({media.Saison:00}x{media.Episode:00})";

                    ToastHelper.Show(title, info, "stop.png", media.BaseUrl);
                }
            }
            catch (Exception ex)
            {
                CoreLog.Write("OnMediaReceived ERROR : " + ex.Message);
            }
        }
    }
}