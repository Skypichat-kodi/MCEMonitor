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
        private NotifyIcon trayIcon      = null!;
        private Timer      watchdog      = null!;
        private Timer      severityTimer = null!;
        private Timer?     startupTimer;            

        private Icon _defaultIcon  = null!;      
        private Icon _warningIcon  = null!;      
        private Icon _criticalIcon = null!;      
        private System.Media.SoundPlayer? _criticalPlayer;   
        private System.Media.SoundPlayer? _warningPlayer;    

        private string _currentSeverity = "";
        private int _startupCheckCount = 0;
        private int _watchdogMisses = 0;

        private const string PIPE_NAME = "MCEMonitor_NetMonitorPipe";

        // ------------------------------------------------------------
        //  Icônes
        // ------------------------------------------------------------
        private void LoadIcons()
        {
            try
            {
                CoreLog.Info("LoadIcons : début (embedded resources)");

                _defaultIcon = EmbeddedResourceLoader.LoadIcon("NetMonitor.ico") ?? SystemIcons.Application;
                _warningIcon = EmbeddedResourceLoader.LoadPngAsIcon("warning.png") ?? _defaultIcon;
                _criticalIcon = EmbeddedResourceLoader.LoadPngAsIcon("critical.png") ?? _defaultIcon;

                CoreLog.Info($"LoadIcons : default={_defaultIcon != null}, warning={_warningIcon != null}, critical={_criticalIcon != null}");
            }
            catch (Exception ex)
            {
                CoreLog.Error($"LoadIcons : {ex}");
                _defaultIcon = SystemIcons.Application;
                _warningIcon = SystemIcons.Application;
                _criticalIcon = SystemIcons.Application;
            }
        }

        // ------------------------------------------------------------
        //  Sons
        // ------------------------------------------------------------
        private void LoadSounds()
        {
            try
            {
                _criticalPlayer = EmbeddedResourceLoader.LoadSound("alarm.wav");
                _warningPlayer = EmbeddedResourceLoader.LoadSound("warning.wav");

                CoreLog.Info($"LoadSounds : critical={_criticalPlayer != null}, warning={_warningPlayer != null}");
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

                byte[] cmdBytes = Encoding.UTF8.GetBytes("get-active");
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

                // Le service renvoie un tableau d'IntrusionEvent (camelCase)
                var alerts = JsonSerializer.Deserialize<List<NetAlert>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (alerts == null) return new List<ProblemInfo>();

                return alerts
                    // ────────────────────────────────────────────────────────
                    //  Filtre : ignore les catégories informatives qui ne sont
                    //  PAS des menaces pour l'utilisateur (scanners de recherche
                    //  légitimes, robots officiels, IP whitelistées…).
                    //  Elles restent visibles dans l'UI WPF et la page Web, mais
                    //  ne polluent plus le popup du tray.
                    // ────────────────────────────────────────────────────────
                    .Where(a => !IsInformativeSeverity(a.severity))
                    .GroupBy(a => a.remoteIp ?? a.source)
                    .Select(g =>
                    {
                        var mostSevere = g.OrderByDescending(x => SeverityRank(x.severity)).First();

                        return new ProblemInfo
                        {
                            Timestamp = mostSevere.timestamp,
                            Severity  = SeverityToTrayLevel(mostSevere.severity),
                            Category  = mostSevere.source ?? "",
                            Message   = mostSevere.message ?? "",
                            Count     = g.Count()
                        };
                    })
                    .OrderByDescending(p => p.Timestamp)
                    .Take(20)
                    .ToList();
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"GetProblems : {ex.Message}");
                return new List<ProblemInfo>();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Helpers de classification des sévérités
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Catégories qui ne représentent PAS une menace utilisateur :
        ///   - Trusted    : scanner de recherche légitime (Shodan, Censys…)
        ///   - Safe       : IP Microsoft/Google vérifiée
        ///   - Bot        : robot officiel (Googlebot, Bingbot…)
        ///   - Crawler    : robot générique (Yandex, Baidu…)
        ///   - Whitelist  : IP marquée Safe manuellement par l'utilisateur
        /// Ces alertes sont visibles dans l'UI mais ne doivent pas faire
        /// passer le tray en orange ni apparaître dans le popup.
        /// </summary>
        private static bool IsInformativeSeverity(string? sev) => sev switch
        {
            "Trusted"     => true,
            "Safe"        => true,
            "Bot"         => true,
            "Crawler"     => true,
            "Whitelist"   => true,
            "Whitelisted" => true,
            _             => false
        };

        /// <summary>
        /// Ranking pour trouver la sévérité la plus grave d'un groupe.
        /// Seules les catégories non-informatives arrivent ici (cf. filtre en amont).
        /// </summary>
        private static int SeverityRank(string? sev) => sev switch
        {
            "Critical"  => 5,
            "High"      => 4,   // legacy → affiché comme Critical
            "ToCheck"   => 3,
            "Warning"   => 2,
            "Info"      => 2,   // legacy → affiché comme Warning
            _           => 0
        };

        /// <summary>
        /// Convertit une sévérité Service en niveau Tray (2 valeurs seulement) :
        ///   - "critical" : Critical + High (legacy)
        ///   - "warning"  : tout le reste (Warning, ToCheck, Info)
        /// </summary>
        private static string SeverityToTrayLevel(string? sev) => sev switch
        {
            "Critical" or "High" => "critical",
            _                    => "warning"
        };

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
                _watchdogMisses++;

                // Tolère 4 échecs consécutifs (60 s) avant de quitter.
                // Laisse le temps à la tâche planifiée SYSTEM de démarrer le service.
                if (_watchdogMisses < 4)
                {
                    CoreLog.Info($"Watchdog : service absent ({_watchdogMisses}/4), on attend...");
                    return;
                }

                CoreLog.Info("Watchdog : service arrêté ? fermeture du Tray");
                trayIcon.Visible = false;
                trayIcon.Dispose();
                Application.Exit();
            }
            else
            {
                _watchdogMisses = 0;
            }
        }

        // ------------------------------------------------------------
        //  Quitter
        // ------------------------------------------------------------
        private void Exit()
        {
            CoreLog.Info("Exit : demande d'arrêt");

            // 1. Envoie shutdown
            try
            {
                using var client = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.Out);
                client.Connect(1000);
                using var writer = new StreamWriter(client);
                writer.WriteLine("shutdown");
                writer.Flush();
            }
            catch (Exception ex)
            {
                CoreLog.Warn($"Exit : shutdown IPC échoué ({ex.Message}) — kill forcé");
            }

            // 2. Attend 5s la fin du cleanup du Service
            var sw = Stopwatch.StartNew();
            bool exitedCleanly = false;
            for (int i = 0; i < 50; i++)
            {
                if (Process.GetProcessesByName("NetMonitor.Service").Length == 0)
                {
                    exitedCleanly = true;
                    break;
                }
                Thread.Sleep(100);
            }

            sw.Stop();
            if (exitedCleanly)
                CoreLog.Info($"Exit : Service arrêté proprement en {sw.ElapsedMilliseconds} ms");
            else
                CoreLog.Warn($"Exit : Service toujours vivant après {sw.ElapsedMilliseconds} ms — kill forcé");

            // 3. Kill brutal si nécessaire
            foreach (var p in Process.GetProcessesByName("NetMonitor.Service"))
            {
                try { p.Kill(); } catch { }
            }

            // 4. Fermer NetMonitor.UI s'il tourne
            foreach (var p in Process.GetProcessesByName("NetMonitor.UI"))
            {
                try { p.Kill(); } catch { }
            }

            // 5. Dispose des timers
            watchdog?.Stop();
            watchdog?.Dispose();
            severityTimer?.Stop();
            severityTimer?.Dispose();
            startupTimer?.Stop();
            startupTimer?.Dispose();

            // 6. Dispose trayIcon
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