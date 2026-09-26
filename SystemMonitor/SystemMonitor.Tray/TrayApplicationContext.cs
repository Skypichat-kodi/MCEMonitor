using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace SystemMonitor.Tray
{
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private Timer watchdog;
        private Timer severityTimer;
        private Timer? startupTimer;

        private Icon _defaultIcon;
        private Icon _warningIcon;
        private Icon _criticalIcon;

        private string _currentSeverity = "";

        private int _startupCheckCount = 0;

        private const string PIPE_NAME = "MCEMonitor_SystemMonitorPipe";

        public TrayApplicationContext()
        {
            LoadIcons();
            InitializeTray();
        }

        // ------------------------------------------------------------
        //  Chargement des icônes
        // ------------------------------------------------------------
        private void LoadIcons()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";

            // Icône par défaut
            string defaultPath = Path.Combine(exeDir, "SystemMonitor.ico");
            _defaultIcon = File.Exists(defaultPath)
                ? new Icon(defaultPath)
                : SystemIcons.Application;

            // Warning
            string warningPath = Path.Combine(exeDir, "warning.png");
            _warningIcon = LoadIconFromPng(warningPath) ?? _defaultIcon;

            // Critical
            string criticalPath = Path.Combine(exeDir, "critical.png");
            _criticalIcon = LoadIconFromPng(criticalPath) ?? _defaultIcon;
        }

        private static Icon? LoadIconFromPng(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                using var bmp = new Bitmap(path);
                IntPtr hIcon = bmp.GetHicon();
                using var tmp = Icon.FromHandle(hIcon);
                return (Icon)tmp.Clone();
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------
        //  Initialisation du Tray
        // ------------------------------------------------------------
        private void InitializeTray()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = _defaultIcon,
                Visible = true,
                Text = "SystemMonitor"
            };

            // Clic gauche ? ouvrir l'UI SystemMonitor
            trayIcon.DoubleClick += (s, e) => OpenSystemMonitorUI();
            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    OpenSystemMonitorUI();
            };

            // Menu contextuel
            var menu = new ContextMenuStrip();
            menu.Items.Add("Ouvrir SystemMonitor", null, (s, e) => OpenSystemMonitorUI());
            menu.Items.Add("Ouvrir MCEMonitor", null, (s, e) => OpenMCEMonitor());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quitter", null, (s, e) => Exit());
            trayIcon.ContextMenuStrip = menu;

            // Watchdog (5s)
            watchdog = new Timer();
            watchdog.Interval = 5000;
            watchdog.Tick += Watchdog_Tick;
            watchdog.Start();

            // Vérification sévérité (30s)
            severityTimer = new Timer();
            severityTimer.Interval = 30000;
            severityTimer.Tick += SeverityTimer_Tick;
            severityTimer.Start();

            // Phase de démarrage : check toutes les 5s pendant 60s
            _startupCheckCount = 0;

            startupTimer = new Timer();
            startupTimer.Interval = 5000;
            startupTimer.Tick += (s, e) =>
            {
                _startupCheckCount++;
                CheckSeverity();

                if (_startupCheckCount >= 12)
                {
                    startupTimer?.Stop();
                    startupTimer?.Dispose();
                    startupTimer = null;
                }
            };
            startupTimer.Start();
        }

        // ------------------------------------------------------------
        //  Timer de sévérité
        // ------------------------------------------------------------
        private void SeverityTimer_Tick(object? sender, EventArgs e)
        {
            CheckSeverity();
        }

        private void CheckSeverity()
        {
            try
            {
                string? severity = GetWorstSeverity();

                if (severity == null)
                    return;

                if (severity == _currentSeverity)
                    return;

                if (trayIcon != null && trayIcon.Visible)
                {
                    trayIcon.Icon = severity switch
                    {
                        "critical" => _criticalIcon,
                        "warning" => _warningIcon,
                        _ => _defaultIcon
                    };

                    trayIcon.Text = severity switch
                    {
                        "critical" => "SystemMonitor - Alerte critique",
                        "warning" => "SystemMonitor - Avertissement",
                        _ => "SystemMonitor"
                    };

                    _currentSeverity = severity;
                }
            }
            catch { }
        }

        // ------------------------------------------------------------
        //  Requête IPC : get-status ? worstSeverity
        // ------------------------------------------------------------
        private string? GetWorstSeverity()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut);

                client.Connect(1000);

                if (!client.IsConnected)
                    return null;

                byte[] cmdBytes = Encoding.UTF8.GetBytes("get-status");
                client.Write(cmdBytes, 0, cmdBytes.Length);
                client.Flush();

                byte[] buffer = new byte[8192];
                int bytesRead = client.Read(buffer, 0, buffer.Length);

                if (bytesRead <= 0)
                    return null;

                string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("worstSeverity", out var sev))
                    return sev.GetString() ?? "ok";

                return "ok";
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------
        //  Ouvrir MCEMonitor
        // ------------------------------------------------------------
        private void OpenMCEMonitor()
        {
            try
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string exePath = Path.Combine(programFiles, "MCEMonitor", "MCEMonitor.exe");

                if (!File.Exists(exePath))
                {
                    string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    exePath = Path.Combine(programFilesX86, "MCEMonitor", "MCEMonitor.exe");
                }

                if (!File.Exists(exePath))
                {
                    MessageBox.Show(
                        "MCEMonitor.exe est introuvable dans Program Files.",
                        "Erreur",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir MCEMonitor : " + ex.Message);
            }
        }

        // ------------------------------------------------------------
        //  Ouvrir SystemMonitor.UI directement
        // ------------------------------------------------------------
        private void OpenSystemMonitorUI()
        {
            try
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string exePath = Path.Combine(programFiles, "MCEMonitor", "SystemMonitor.UI.exe");

                if (!File.Exists(exePath))
                {
                    string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    exePath = Path.Combine(programFilesX86, "MCEMonitor", "SystemMonitor.UI.exe");
                }

                if (!File.Exists(exePath))
                {
                    MessageBox.Show(
                        "SystemMonitor.UI.exe est introuvable dans Program Files.",
                        "Erreur",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--from-mcem",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir SystemMonitor.UI : " + ex.Message);
            }
        }

        // ------------------------------------------------------------
        //  Watchdog
        // ------------------------------------------------------------
        private void Watchdog_Tick(object? sender, EventArgs e)
        {
            bool serviceRunning = Process.GetProcessesByName("SystemMonitor.Service").Any();

            if (!serviceRunning)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                Application.Exit();
            }
        }

        // ------------------------------------------------------------
        //  Quitter
        // ------------------------------------------------------------
        private void Exit()
        {
            // 1. Envoyer "shutdown" au service via IPC
            try
            {
                using var client = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.Out);
                client.Connect(1000);

                using var writer = new StreamWriter(client);
                writer.WriteLine("shutdown");
                writer.Flush();

                Thread.Sleep(800);
            }
            catch { }

            // 2. Si le service tourne encore ? kill
            for (int i = 0; i < 20; i++)
            {
                if (Process.GetProcessesByName("SystemMonitor.Service").Length == 0)
                    break;
                Thread.Sleep(100);
            }

            foreach (var p in Process.GetProcessesByName("SystemMonitor.Service"))
            {
                try { p.Kill(); } catch { }
            }

            // 3. Fermer SystemMonitor.UI s'il tourne
            foreach (var p in Process.GetProcessesByName("SystemMonitor.UI"))
            {
                try { p.Kill(); } catch { }
            }

            // 4. Fermer le Tray
            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }
    }
}