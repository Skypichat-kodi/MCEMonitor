using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using System.IO.Pipes;
using System.Threading;
using Timer = System.Windows.Forms.Timer;

namespace RomMonitor.Tray
{
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private Timer watchdog;

        public TrayApplicationContext()
        {
            InitializeTray();
        }

        private void InitializeTray()
        {
            // ------------------------------------------------------------
            // Chargement de l'icône
            // ------------------------------------------------------------
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            string iconPath = Path.Combine(exeDir, "RomMonitor.ico");

            trayIcon = new NotifyIcon()
            {
                Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
                Visible = true,
                Text = "RomMonitor"
            };

            // ------------------------------------------------------------
            // Gestion des clics : double-clic ou clic gauche ? ouvre l'UI
            // ------------------------------------------------------------
            trayIcon.DoubleClick += (s, e) => OpenRomMonitorUI();
            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    OpenRomMonitorUI();
            };

            // ------------------------------------------------------------
            // Menu contextuel
            // ------------------------------------------------------------
            var menu = new ContextMenuStrip();

            menu.Items.Add("Ouvrir RomMonitor", null, (s, e) => OpenRomMonitorUI());
            menu.Items.Add("Ouvrir MCEMonitor", null, (s, e) => OpenMCEMonitor());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quitter", null, (s, e) => Exit());

            trayIcon.ContextMenuStrip = menu;

            // ------------------------------------------------------------
            // Watchdog : vérifie toutes les 5 secondes si le service tourne
            // ------------------------------------------------------------
            watchdog = new Timer();
            watchdog.Interval = 5000;
            watchdog.Tick += Watchdog_Tick;
            watchdog.Start();
        }

        // ------------------------------------------------------------
        // Ouvrir RomMonitor.UI
        // ------------------------------------------------------------
        private void OpenRomMonitorUI()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                string uiPath = Path.Combine(exeDir, "RomMonitor.UI.exe");

                if (!File.Exists(uiPath))
                {
                    // Tentative dans Program Files
                    string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    uiPath = Path.Combine(programFiles, "MCEMonitor", "RomMonitor.UI.exe");
                }

                if (!File.Exists(uiPath))
                {
                    MessageBox.Show(
                        "RomMonitor.UI.exe est introuvable.",
                        "Erreur",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = uiPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir RomMonitor : " + ex.Message);
            }
        }

        // ------------------------------------------------------------
        // Ouvrir MCEMonitor
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
        // Watchdog : ferme le Tray si le service s'arrête
        // ------------------------------------------------------------
        private void Watchdog_Tick(object sender, EventArgs e)
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
        // Quitter proprement (IPC ? service + fermeture UI)
        // ------------------------------------------------------------
        private void Exit()
        {
            // 1. Envoyer "shutdown" au service RomMonitor via IPC
            bool shutdownSent = false;

            try
            {
                using var client = new NamedPipeClientStream(".", "MCEMonitor_RomMonitorPipe", PipeDirection.Out);
                client.Connect(1000);   // 1s au lieu de 500ms

                using var writer = new StreamWriter(client);
                writer.WriteLine("shutdown");
                writer.Flush();

                shutdownSent = true;

                // Attendre la réponse du service
                Thread.Sleep(500);
            }
            catch
            {
                // IPC échoué ? on tuera le processus plus bas
            }

            // 2. Attendre que le service s'arrête (max 2 secondes)
            if (shutdownSent)
            {
                for (int i = 0; i < 20; i++)   // 20 × 100ms = 2s
                {
                    if (Process.GetProcessesByName("RomMonitor.Service").Length == 0)
                        break;

                    Thread.Sleep(100);
                }
            }

            // 3. Si le service tourne encore ? kill direct
            foreach (var p in Process.GetProcessesByName("RomMonitor.Service"))
            {
                try { p.Kill(); } catch { }
            }

            // 4. Fermer RomMonitor.UI.exe s'il tourne
            foreach (var p in Process.GetProcessesByName("RomMonitor.UI"))
            {
                try { p.Kill(); } catch { }
            }

            // 5. Fermer le Tray
            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }
    }
}