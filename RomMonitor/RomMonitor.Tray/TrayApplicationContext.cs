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
using System.Collections.Generic;
using Timer = System.Windows.Forms.Timer;

namespace RomMonitor.Tray
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

        // Compteur pour la phase de démarrage (check toutes les 5s pendant 60s)
        private int _startupCheckCount = 0;

        private const string PIPE_NAME = "MCEMonitor_RomMonitorPipe";

        public TrayApplicationContext()
        {
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

            // Clic gauche / double-clic ? ouvrir le popup
            trayIcon.DoubleClick += (s, e) => ShowProblemsPopup();
            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    ShowProblemsPopup();
            };

            // Menu contextuel
            var menu = new ContextMenuStrip();
            menu.Items.Add("Ouvrir RomMonitor", null, (s, e) => OpenRomMonitorUI());
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

            startupTimer = new Timer();
            startupTimer.Interval = 5000;
            startupTimer.Tick += (s, e) =>
            {
                _startupCheckCount++;
                CheckSeverity();

                if (_startupCheckCount >= 12)   // 12 × 5s = 60s
                {
                    startupTimer?.Stop();
                    startupTimer?.Dispose();
                    startupTimer = null;
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

                if (trayIcon != null && trayIcon.Visible)
                {
                    // On est déjà sur le thread UI grâce au Timer WinForms,
                    // donc pas besoin de marshalling.
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

                    // Son uniquement lors d'une transition vers un état d'alerte.
                    // On ne joue rien au premier check (démarrage), ni au retour à "ok".
                    if (!string.IsNullOrEmpty(_currentSeverity))
                    {
                        if (severity == "critical")
                        {
                            System.Media.SystemSounds.Hand.Play();
                        }
                        else if (severity == "warning")
                        {
                            System.Media.SystemSounds.Exclamation.Play();
                        }
                    }

                    // On mémorise la sévérité
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
        //  Ouvrir RomMonitor.UI directement
        // ------------------------------------------------------------
        private void OpenRomMonitorUI()
        {
            try
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string exePath = Path.Combine(programFiles, "MCEMonitor", "RomMonitor.UI.exe");

                if (!File.Exists(exePath))
                {
                    string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    exePath = Path.Combine(programFilesX86, "MCEMonitor", "RomMonitor.UI.exe");
                }

                if (!File.Exists(exePath))
                {
                    MessageBox.Show(
                        "RomMonitor.UI.exe est introuvable dans Program Files.",
                        "Erreur",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                string lang = "fr-FR";

                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--from-mcem -lang {lang}",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir RomMonitor.UI : " + ex.Message);
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
        
        // ------------------------------------------------------------
        //  Requête IPC : get-alerts ? liste des problèmes
        // ------------------------------------------------------------
        private List<ProblemInfo> GetProblems()
        {
            try
            {
                // ---- 1) Seuils depuis la config ----
                int warnPct = 15, critPct = 5, warnGo = 10, critGo = 5;

                using (var c = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut))
                {
                    c.Connect(1000);
                    if (!c.IsConnected) return new List<ProblemInfo>();

                    byte[] cmd = Encoding.UTF8.GetBytes("get-config");
                    c.Write(cmd, 0, cmd.Length);
                    c.Flush();

                    byte[] buf = new byte[8192];
                    int n = c.Read(buf, 0, buf.Length);
                    string json = Encoding.UTF8.GetString(buf, 0, n);

                    using var doc = JsonDocument.Parse(json);
                    var r = doc.RootElement;

                    if (r.TryGetProperty("diskSpaceWarnPercent", out var vWarnPct))
                        warnPct = vWarnPct.GetInt32();

                    if (r.TryGetProperty("diskSpaceCriticalPercent", out var vCritPct))
                        critPct = vCritPct.GetInt32();

                    if (r.TryGetProperty("diskSpaceWarnGo", out var vWarnGo))
                        warnGo = vWarnGo.GetInt32();

                    if (r.TryGetProperty("diskSpaceCriticalGo", out var vCritGo))
                        critGo = vCritGo.GetInt32();
                }

                var problems = new List<ProblemInfo>();

                // ---- 2) Disques (état actuel) ----
                using (var c = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut))
                {
                    c.Connect(1000);
                    if (!c.IsConnected) return new List<ProblemInfo>();

                    byte[] cmd = Encoding.UTF8.GetBytes("get-disks");
                    c.Write(cmd, 0, cmd.Length);
                    c.Flush();

                    using var mem = new MemoryStream();
                    byte[] buf = new byte[16384];
                    while (true)
                    {
                        int n = c.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        mem.Write(buf, 0, n);
                    }

                    using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(mem.ToArray()));

                    foreach (var d in doc.RootElement.EnumerateArray())
                    {
                        string name  = d.GetProperty("name").GetString() ?? "";
                        double pct   = d.GetProperty("freePercent").GetDouble();
                        double go    = d.GetProperty("freeGo").GetDouble();

                        bool critical = pct < critPct || go < critGo;
                        bool warning  = pct < warnPct  || go < warnGo;

                        if (critical)
                        {
                            problems.Add(new ProblemInfo
                            {
                                Timestamp = DateTime.Now,
                                Severity  = "critical",
                                Category  = "DiskSpaceCritical",
                                Message   = $"Espace critique : {pct:F1}% ({go:F1} Go libre) sur {name}"
                            });
                        }
                        else if (warning)
                        {
                            problems.Add(new ProblemInfo
                            {
                                Timestamp = DateTime.Now,
                                Severity  = "warning",
                                Category  = "DiskSpaceLow",
                                Message   = $"Espace faible : {pct:F1}% ({go:F1} Go libre) sur {name}"
                            });
                        }
                    }
                }

                // ---- 3) SMART (état actuel) ----
                using (var c = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut))
                {
                    c.Connect(1000);
                    if (!c.IsConnected) return problems;

                    byte[] cmd = Encoding.UTF8.GetBytes("get-smart");
                    c.Write(cmd, 0, cmd.Length);
                    c.Flush();

                    using var mem = new MemoryStream();
                    byte[] buf = new byte[16384];
                    while (true)
                    {
                        int n = c.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        mem.Write(buf, 0, n);
                    }

                    using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(mem.ToArray()));

                    foreach (var s in doc.RootElement.EnumerateArray())
                    {
                        string status = s.GetProperty("status").GetString() ?? "";
                        if (status != "Critical" && status != "Warning") continue;

                        string model  = s.GetProperty("model").GetString() ?? "";
                        string reason = s.GetProperty("statusReason").GetString() ?? "";

                        problems.Add(new ProblemInfo
                        {
                            Timestamp = DateTime.Now,
                            Severity  = status == "Critical" ? "critical" : "warning",
                            Category  = "SmartFailure",
                            Message   = $"{model} : {reason}"
                        });
                    }
                }

                return problems
                    .OrderByDescending(p => p.Severity == "critical")
                    .ThenBy(p => p.Category)
                    .Take(20)
                    .ToList();
            }
            catch
            {
                return new List<ProblemInfo>();
            }
        }
        
        // ------------------------------------------------------------
        //  Afficher le popup des problèmes
        // ------------------------------------------------------------
        private void ShowProblemsPopup()
        {
            try
            {
                var problems = GetProblems();

                var popup = new ProblemsPopupForm(problems);

                popup.OnOpenRomMonitor += () => OpenRomMonitorUI();
                popup.OnOpenMCEMonitor += () => OpenMCEMonitor();
                popup.OnQuit += () => Exit();

                popup.FormClosed += (s, e) => popup.Dispose();

                popup.ShowAtTrayPosition();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Erreur lors de l'ouverture du popup : " + ex.Message,
                    "Erreur",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }                
    }
}