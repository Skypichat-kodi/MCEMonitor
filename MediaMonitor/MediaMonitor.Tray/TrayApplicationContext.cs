using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using System.IO.Pipes;

namespace MediaMonitor.Tray
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
            string iconPath = Path.Combine(exeDir, "MediaMonitor.ico");

            trayIcon = new NotifyIcon()
            {
                Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
                Visible = true,
                Text = "MediaMonitor"
            };

            // ------------------------------------------------------------
            // Gestion des clics ? ouvrir l'UI MediaMonitor
            // ------------------------------------------------------------
            trayIcon.DoubleClick += (s, e) => OpenMediaMonitorUI();
            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    OpenMediaMonitorUI();
            };

            // ------------------------------------------------------------
            // Menu contextuel
            // ------------------------------------------------------------
            var menu = new ContextMenuStrip();
            menu.Items.Add("Ouvrir MediaMonitor", null, (s, e) => OpenMediaMonitorUI());
            menu.Items.Add("Ouvrir MCEMonitor", null, (s, e) => OpenMCEMonitor());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quitter", null, (s, e) => Exit());

            trayIcon.ContextMenuStrip = menu;

            // ------------------------------------------------------------
            // Watchdog
            // ------------------------------------------------------------
            watchdog = new Timer();
            watchdog.Interval = 5000;
            watchdog.Tick += Watchdog_Tick;
            watchdog.Start();
        }

        // ------------------------------------------------------------
        //  Ouvrir MediaMonitor.UI
        // ------------------------------------------------------------
        private void OpenMediaMonitorUI()
        {
            try
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string exePath = Path.Combine(programFiles, "MCEMonitor", "MediaMonitor.UI.exe");

                if (!File.Exists(exePath))
                {
                    string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    exePath = Path.Combine(programFilesX86, "MCEMonitor", "MediaMonitor.UI.exe");
                }

                if (!File.Exists(exePath))
                {
                    MessageBox.Show(
                        "MediaMonitor.UI.exe est introuvable dans Program Files.",
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
                MessageBox.Show("Impossible d'ouvrir MediaMonitor.UI : " + ex.Message);
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
        //  Watchdog
        // ------------------------------------------------------------
        private void Watchdog_Tick(object sender, EventArgs e)
        {
            bool serviceRunning = Process.GetProcessesByName("MediaMonitor.Service").Any();

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
            try
            {
                using var client = new NamedPipeClientStream(".", "MediaMonitorPipe", PipeDirection.Out);
                client.Connect(500);

                using var writer = new StreamWriter(client);
                writer.WriteLine("shutdown");
                writer.Flush();
            }
            catch
            {
                foreach (var p in Process.GetProcessesByName("MediaMonitor.Service"))
                {
                    try { p.Kill(); } catch { }
                }
            }

            foreach (var p in Process.GetProcessesByName("MediaMonitor.UI"))
            {
                try { p.Kill(); } catch { }
            }

            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }
    }
}