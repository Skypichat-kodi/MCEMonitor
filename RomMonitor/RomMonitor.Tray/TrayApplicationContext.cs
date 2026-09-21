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

namespace RomMonitor.Tray
{
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private Timer watchdog;
        private Timer severityTimer;

        private Icon _defaultIcon;
        private Icon _warningIcon;
        private Icon _criticalIcon;

        private string _currentSeverity = "";

        private readonly SynchronizationContext? _uiContext;

        // Compteur pour la phase de démarrage (check toutes les 5s pendant 60s)
        private int _startupCheckCount = 0;

        private const string PIPE_NAME = "MCEMonitor_RomMonitorPipe";

        public TrayApplicationContext()
        {
            // Capture du contexte UI pour marshaller les mises à jour d'icône
            _uiContext = SynchronizationContext.Current;

            LoadIcons();
            InitializeTray();
        }

        // ------------------------------------------------------------
        //  Chargement des icônes (depuis PNG ou ICO)
        // ------------------------------------------------------------
        private void LoadIcons()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";

            // Icône par défaut (RomMonitor.ico à côté du Tray)
            string defaultPath = Path.Combine(exeDir, "RomMonitor.ico");
            _defaultIcon = File.Exists(defaultPath)
                ? new Icon(defaultPath)
                : SystemIcons.Application;

            // Icône Warning (PNG)
            string warningPath = Path.Combine(exeDir, "warning.png");
            _warningIcon = LoadIconFromPng(warningPath) ?? _defaultIcon;

            // Icône Critical (PNG)
            string criticalPath = Path.Combine(exeDir, "critical.png");
            _criticalIcon = LoadIconFromPng(criticalPath) ?? _defaultIcon;
        }

        /// <summary>
        /// Charge une icône depuis un PNG et la convertit en Icon.
        /// </summary>
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
                Text = "RomMonitor"
            };

            // Double-clic / clic gauche ? ouvrir MCEMonitor
            trayIcon.DoubleClick += (s, e) => OpenMCEMonitor();
            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    OpenMCEMonitor();
            };

            // Menu contextuel
            var menu = new ContextMenuStrip();
            menu.Items.Add("Ouvrir MCEMonitor", null, (s, e) => OpenMCEMonitor());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quitter", null, (s, e) => Exit());

            trayIcon.ContextMenuStrip = menu;

            // Watchdog : vérifie que le service tourne (5s)
            watchdog = new Timer();
            watchdog.Interval = 5000;
            watchdog.Tick += Watchdog_Tick;
            watchdog.Start();

            // Vérification de la sévérité (30s) — régime stable
            severityTimer = new Timer();
            severityTimer.Interval = 30000;
            severityTimer.Tick += SeverityTimer_Tick;
            severityTimer.Start();

            // ------------------------------------------------------------
            // Phase de démarrage : check toutes les 5s pendant 60s
            // Le service peut mettre ~40s à faire son premier scan SMART.
            // ------------------------------------------------------------
            _startupCheckCount = 0;

            var startupTimer = new Timer();
            startupTimer.Interval = 5000;
            startupTimer.Tick += (s, e) =>
            {
                _startupCheckCount++;
                CheckSeverity();

                if (_startupCheckCount >= 12)   // 12 × 5s = 60s
                {
                    startupTimer.Stop();
                    startupTimer.Dispose();
                }
            };
            startupTimer.Start();
        }

        // ------------------------------------------------------------
        //  Timer de sévérité ? change l'icône (régime stable)
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
                    return;   // service non joignable ? on ne touche pas

                if (severity == _currentSeverity)
                    return;   // pas de changement

                _currentSeverity = severity;

                // Changer l'icône (sur le thread UI)
                if (trayIcon != null && trayIcon.Visible)
                {
                    _uiContext?.Post(_ =>
                    {
                        if (trayIcon == null || !trayIcon.Visible)
                            return;

                        trayIcon.Icon = severity switch
                        {
                            "critical" => _criticalIcon,
                            "warning"  => _warningIcon,
                            _          => _defaultIcon
                        };

                        trayIcon.Text = severity switch
                        {
                            "critical" => "RomMonitor - Alerte critique",
                            "warning"  => "RomMonitor - Avertissement",
                            _          => "RomMonitor"
                        };
                    }, null);
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

                // Connect avec timeout (lève une exception si timeout)
                client.Connect(1000);

                if (!client.IsConnected)
                    return null;

                // Envoyer la commande
                byte[] cmdBytes = Encoding.UTF8.GetBytes("get-status");
                client.Write(cmdBytes, 0, cmdBytes.Length);
                client.Flush();

                // Lire la réponse
                byte[] buffer = new byte[8192];
                int bytesRead = client.Read(buffer, 0, buffer.Length);

                if (bytesRead <= 0)
                    return null;

                string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                // Parser le JSON
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
        //  Watchdog : ferme le Tray si le service s'arrête
        // ------------------------------------------------------------
        private void Watchdog_Tick(object? sender, EventArgs e)
        {
            bool serviceRunning = Process.GetProcessesByName("RomMonitor.Service").Any();

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
            // 1. Envoyer "shutdown" au service RomMonitor via IPC
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
                if (Process.GetProcessesByName("RomMonitor.Service").Length == 0)
                    break;
                Thread.Sleep(100);
            }

            foreach (var p in Process.GetProcessesByName("RomMonitor.Service"))
            {
                try { p.Kill(); } catch { }
            }

            // 3. Fermer RomMonitor.UI s'il tourne
            foreach (var p in Process.GetProcessesByName("RomMonitor.UI"))
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