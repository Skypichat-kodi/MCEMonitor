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

        // --- Sons ---
        private System.Media.SoundPlayer? _alarmPlayer;
        private System.Media.SoundPlayer? _warningPlayer;

        public TrayApplicationContext()
        {
            CoreLog.Clear();
            CoreLog.Write("=== Tray démarré ===");

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

            // Icône de l'exe (à la racine du dossier de sortie)
            string ico = Path.Combine(exeDir, "MCEMonitorClient.ico");
            var defaultIcon = File.Exists(ico) ? new Icon(ico) : SystemIcons.Application;

            // Icônes d'état (dans Resources\Icons)
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

            // Sons (dans Resources\Sounds)
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
            _pushListener.Start();
        }

        private void OnAlertReceived(PushAlert alert)
        {
            // Met à jour l'icône selon le statut
            UpdateIconFromAlert(alert);

            // Notification Windows
            ShowNotification(alert);
        }

        private void UpdateIconFromAlert(PushAlert alert)
        {
            // ? Icône : on ne change que si l'état global a évolué
            // Pour simplifier, on met l'icône selon le statut du serveur concerné
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
        //  Notifications
        // ---------------------------------------------
        private void ShowNotification(PushAlert alert)
        {
            try
            {
                string title, message;
                ToolTipIcon icon;

                switch (alert.Status)
                {
                    case "critical":
                        title = $"{alert.ServerName} - CRITIQUE";
                        message = string.IsNullOrEmpty(alert.FirstProblem)
                            ? "Problème critique détecté"
                            : alert.FirstProblem;
                        icon = ToolTipIcon.Error;
                        PlayAlarm();
                        break;

                    case "warning":
                        title = $"{alert.ServerName} - Alerte";
                        message = string.IsNullOrEmpty(alert.FirstProblem)
                            ? "Avertissement détecté"
                            : alert.FirstProblem;
                        icon = ToolTipIcon.Warning;
                        break;

                    case "offline":
                        title = $"{alert.ServerName} - Hors ligne";
                        message = "Le serveur ne répond plus";
                        icon = ToolTipIcon.Error;
                        PlayAlarm();
                        break;

                    case "ok" when alert.PreviousStatus != "ok":
                        title = $"{alert.ServerName} - Retour à la normale";
                        message = "Le serveur est de nouveau joignable";
                        icon = ToolTipIcon.Info;
                        break;

                    default:
                        return;   // Pas de notif si on reste en "ok"
                }

                _trayIcon.BalloonTipTitle = title;
                _trayIcon.BalloonTipText = message;
                _trayIcon.BalloonTipIcon = icon;

                // Clic sur la notif ? ouvrir l'URL
                _trayIcon.BalloonTipClicked -= BalloonClickedHandler;
                _trayIcon.Tag = alert.BaseUrl;   // stocke l'URL pour le handler
                _trayIcon.BalloonTipClicked += BalloonClickedHandler;

                _trayIcon.ShowBalloonTip(10000);
            }
            catch (Exception ex)
            {
                CoreLog.Write("ShowNotification ERROR : " + ex.Message);
            }
        }

        private void BalloonClickedHandler(object? sender, EventArgs e)
        {
            try
            {
                string? url = _trayIcon.Tag as string;

                if (!string.IsNullOrWhiteSpace(url))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            }
            catch { }
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
            _pushListener.Stop();
            _pollTimer?.Stop();

            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            CoreLog.Write("Tray quitté");

            Application.Exit();
        }
    }
}