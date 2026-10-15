using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using NetMonitor.Tray.Logs;
using Timer = System.Windows.Forms.Timer;

namespace NetMonitor.Tray
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
        private System.Media.SoundPlayer? _criticalPlayer;
        private System.Media.SoundPlayer? _warningPlayer;

        private string _currentSeverity = "";
        private int _startupCheckCount = 0;

        private const string PIPE_NAME = "MCEMonitor_NetMonitorPipe";

        // ------------------------------------------------------------
        //  Icônes
        // ------------------------------------------------------------
        private void LoadIcons()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
                CoreLog.Info($"LoadIcons : exeDir = {exeDir}");

                string defaultPath = Path.Combine(exeDir, "NetMonitor.ico");
                CoreLog.Info($"LoadIcons : NetMonitor.ico existe = {File.Exists(defaultPath)}");

                _defaultIcon = SafeLoadIcon(defaultPath) ?? SystemIcons.Application;

                string warningPath = Path.Combine(exeDir, "warning.png");
                CoreLog.Info($"LoadIcons : warning.png existe = {File.Exists(warningPath)}");
                _warningIcon = LoadIconFromPng(warningPath) ?? _defaultIcon;

                string criticalPath = Path.Combine(exeDir, "critical.png");
                CoreLog.Info($"LoadIcons : critical.png existe = {File.Exists(criticalPath)}");
                _criticalIcon = LoadIconFromPng(criticalPath) ?? _defaultIcon;

                CoreLog.Info("LoadIcons : OK");
            }
            catch (Exception ex)
            {
                CoreLog.Error($"LoadIcons : {ex}");
                _defaultIcon = SystemIcons.Application;
                _warningIcon = SystemIcons.Application;
                _criticalIcon = SystemIcons.Application;
            }
        }

        private static Icon? SafeLoadIcon(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                // Copie en mémoire pour éviter tout verrou fichier,
                // puis création de l'icône depuis le flux en mémoire.
                byte[] bytes = File.ReadAllBytes(path);
                using var ms = new MemoryStream(bytes);
                return new Icon(ms);
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"SafeLoadIcon('{path}') : {ex.Message}");
                return null;
            }
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
            catch (Exception ex)
            {
                CoreLog.Warn($"LoadIconFromPng('{path}') : {ex.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------
        //  Sons
        // ------------------------------------------------------------
        private void LoadSounds()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";

                string crit = Path.Combine(exeDir, "alarm.wav");
                if (File.Exists(crit))
                    _criticalPlayer = new System.Media.SoundPlayer(crit);

                string warn = Path.Combine(exeDir, "warning.wav");
                if (File.Exists(warn))
                    _warningPlayer = new System.Media.SoundPlayer(warn);

                CoreLog.Info($"LoadSounds : critical={( _criticalPlayer != null )}, warning={( _warningPlayer != null )}");
            }
            catch (Exception ex)
            {
                CoreLog.Error($"LoadSounds : {ex.Message}");
            }
        }

        public TrayApplicationContext()
        {
            try
            {
                CoreLog.Info("==== TrayApplicationContext : constructeur ====");

                LoadIcons();
                LoadSounds();
                InitializeTray();

                CoreLog.Info("==== TrayApplicationContext : prêt ====");
            }
            catch (Exception ex)
            {
                CoreLog.Error($"Constructeur : {ex}");
                throw;
            }
        }

        // ------------------------------------------------------------
        //  Initialisation Tray
        // ------------------------------------------------------------
        private void InitializeTray()
        {
            try
            {
                CoreLog.Info("InitializeTray : début");

                trayIcon = new NotifyIcon()
                {
                    Icon = _defaultIcon ?? SystemIcons.Application,
                    Visible = true,
                    Text = "NetMonitor"
                };

                CoreLog.Info("InitializeTray : NotifyIcon créé (Visible=true)");

                trayIcon.DoubleClick += (s, e) => ShowProblemsPopup();
                trayIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                        ShowProblemsPopup();
                };

                var menu = new ContextMenuStrip();
                menu.Items.Add("Ouvrir NetMonitor", null, (s, e) => OpenNetMonitorUI());
                menu.Items.Add("Ouvrir MCEMonitor", null, (s, e) => OpenMCEMonitor());
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Quitter", null, (s, e) => Exit());
                trayIcon.ContextMenuStrip = menu;

                watchdog = new Timer { Interval = 5000 };
                watchdog.Tick += Watchdog_Tick;
                watchdog.Start();

                severityTimer = new Timer { Interval = 30000 };
                severityTimer.Tick += (s, e) => CheckSeverity();
                severityTimer.Start();

                _startupCheckCount = 0;
                startupTimer = new Timer { Interval = 5000 };
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

                CoreLog.Info("InitializeTray : OK");
            }
            catch (Exception ex)
            {
                CoreLog.Error($"InitializeTray : {ex}");
                throw;
            }
        }

        // ------------------------------------------------------------
        //  Sévérité
        // ------------------------------------------------------------
        private void CheckSeverity()
        {
            try
            {
                string? severity = GetWorstSeverity();
                if (severity == null) return;
                if (severity == _currentSeverity) return;

                if (trayIcon != null && trayIcon.Visible)
                {
                    trayIcon.Icon = severity switch
                    {
                        "critical" => _criticalIcon ?? SystemIcons.Application,
                        "warning"  => _warningIcon  ?? SystemIcons.Application,
                        _          => _defaultIcon  ?? SystemIcons.Application
                    };

                    trayIcon.Text = severity switch
                    {
                        "critical" => "NetMonitor - Alerte critique",
                        "warning"  => "NetMonitor - Avertissement",
                        _          => "NetMonitor"
                    };

                    if (!string.IsNullOrEmpty(_currentSeverity))
                    {
                        if (severity == "critical") PlayCriticalAlarm();
                        else if (severity == "warning") PlayWarningAlarm();
                    }

                    _currentSeverity = severity;
                    CoreLog.Info($"Severity ? {severity}");
                }
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"CheckSeverity : {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        //  IPC
        // ------------------------------------------------------------
        private List<ProblemInfo> GetProblems()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut);
                client.Connect(1000);
                if (!client.IsConnected) return new List<ProblemInfo>();

                byte[] cmdBytes = Encoding.UTF8.GetBytes("get-problems");
                client.Write(cmdBytes, 0, cmdBytes.Length);
                client.Flush();

                byte[] buffer = new byte[16384];
                using var mem = new MemoryStream();

                while (true)
                {
                    int bytesRead = client.Read(buffer, 0, buffer.Length);
                    if (bytesRead <= 0) break;
                    mem.Write(buffer, 0, bytesRead);
                }

                string json = Encoding.UTF8.GetString(mem.ToArray());
                if (string.IsNullOrWhiteSpace(json)) return new List<ProblemInfo>();

                var problems = JsonSerializer.Deserialize<List<ProblemInfo>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return problems ?? new List<ProblemInfo>();
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"GetProblems : {ex.Message}");
                return new List<ProblemInfo>();
            }
        }

        private string? GetWorstSeverity()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut);
                client.Connect(1000);
                if (!client.IsConnected) return null;

                byte[] cmdBytes = Encoding.UTF8.GetBytes("get-status");
                client.Write(cmdBytes, 0, cmdBytes.Length);
                client.Flush();

                byte[] buffer = new byte[8192];
                int bytesRead = client.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0) return null;

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
        //  Ouvrir UI
        // ------------------------------------------------------------
        private void OpenNetMonitorUI()
        {
            try
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string exePath = Path.Combine(programFiles, "MCEMonitor", "NetMonitor.UI.exe");

                if (!File.Exists(exePath))
                {
                    string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    exePath = Path.Combine(x86, "MCEMonitor", "NetMonitor.UI.exe");
                }

                if (!File.Exists(exePath))
                {
                    MessageBox.Show("NetMonitor.UI.exe est introuvable dans Program Files.",
                        "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                MessageBox.Show("Impossible d'ouvrir NetMonitor.UI : " + ex.Message);
            }
        }

        private void OpenMCEMonitor()
        {
            try
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string exePath = Path.Combine(programFiles, "MCEMonitor", "MCEMonitor.exe");

                if (!File.Exists(exePath))
                {
                    string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    exePath = Path.Combine(x86, "MCEMonitor", "MCEMonitor.exe");
                }

                if (!File.Exists(exePath))
                {
                    MessageBox.Show("MCEMonitor.exe est introuvable dans Program Files.",
                        "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir MCEMonitor : " + ex.Message);
            }
        }

        // ------------------------------------------------------------
        //  Watchdog
        // ------------------------------------------------------------
        private void Watchdog_Tick(object? sender, EventArgs e)
        {
            bool serviceRunning = Process.GetProcessesByName("NetMonitor.Service").Any();

            if (!serviceRunning)
            {
                CoreLog.Info("Watchdog : service arrêté ? fermeture du Tray");
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
            CoreLog.Info("Exit : demande d'arrêt");

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

            for (int i = 0; i < 20; i++)
            {
                if (Process.GetProcessesByName("NetMonitor.Service").Length == 0)
                    break;
                Thread.Sleep(100);
            }

            foreach (var p in Process.GetProcessesByName("NetMonitor.Service"))
            {
                try { p.Kill(); } catch { }
            }

            foreach (var p in Process.GetProcessesByName("NetMonitor.UI"))
            {
                try { p.Kill(); } catch { }
            }

            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }

        // ------------------------------------------------------------
        //  Popup
        // ------------------------------------------------------------
        private void ShowProblemsPopup()
        {
            try
            {
                var problems = GetProblems();
                var popup = new ProblemsPopupForm(problems);

                popup.OnOpenNetMonitor += () => OpenNetMonitorUI();
                popup.OnOpenMCEMonitor += () => OpenMCEMonitor();
                popup.OnQuit += () => Exit();

                popup.FormClosed += (s, e) => popup.Dispose();
                popup.ShowAtTrayPosition();
            }
            catch (Exception ex)
            {
                CoreLog.Error($"ShowProblemsPopup : {ex.Message}");
                MessageBox.Show("Erreur lors de l'ouverture du popup : " + ex.Message,
                    "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------
        //  Sons
        // ------------------------------------------------------------
        private void PlayCriticalAlarm()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (_criticalPlayer != null)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            _criticalPlayer.PlaySync();
                            Thread.Sleep(150);
                        }
                        return;
                    }

                    for (int rep = 0; rep < 3; rep++)
                    {
                        for (int f = 700; f <= 1800; f += 80) Console.Beep(f, 35);
                        for (int f = 1800; f >= 700; f -= 80) Console.Beep(f, 35);
                        Thread.Sleep(120);
                    }

                    Console.Beep(2200, 250);
                    Console.Beep(1100, 250);
                    Console.Beep(2200, 250);
                    Console.Beep(1100, 400);
                }
                catch { }
            });
        }

        private void PlayWarningAlarm()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (_warningPlayer != null)
                    {
                        _warningPlayer.PlaySync();
                        return;
                    }

                    Console.Beep(1200, 150);
                    Thread.Sleep(80);
                    Console.Beep(1200, 150);
                }
                catch { }
            });
        }
    }
}