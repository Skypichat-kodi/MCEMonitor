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
            // ------------------------------------------------------------
            // Chargement de l'icône
            // ------------------------------------------------------------
            string iconPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "MediaMonitor.ico"
            );

            trayIcon = new NotifyIcon()
            {
                Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
                Visible = true,
                Text = "MediaMonitor"
            };

            // ------------------------------------------------------------
            // Gestion des clics
            // ------------------------------------------------------------
            trayIcon.DoubleClick += (s, e) => OpenUI();
            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    OpenUI();
            };

            // ------------------------------------------------------------
            // Menu contextuel
            // ------------------------------------------------------------
            var menu = new ContextMenuStrip();
            menu.Items.Add("Ouvrir MediaMonitor", null, (s, e) => OpenUI());
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
        // Ouvrir l’UI
        // ------------------------------------------------------------
        private void OpenUI()
        {
            try
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string uiPath = Path.Combine(programFiles, "MCEMonitor", "MediaMonitor.UI.exe");

                if (!File.Exists(uiPath))
                {
                    string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    uiPath = Path.Combine(programFilesX86, "MCEMonitor", "MediaMonitor.UI.exe");
                }

                if (!File.Exists(uiPath))
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
                    FileName = uiPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir l'UI : " + ex.Message);
            }
        }

        // ------------------------------------------------------------
        // Watchdog : ferme le Tray si le service s'arrête
        // ------------------------------------------------------------
        private void Watchdog_Tick(object sender, EventArgs e)
        {
            bool serviceRunning = Process.GetProcessesByName("MediaMonitor.Service").Any();

            if (!serviceRunning)
            {
                trayIcon.Visible = false;
                Application.Exit();
            }
        }

        // ------------------------------------------------------------
        // Quitter proprement (IPC vers le service + fermeture UI)
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

            // ------------------------------------------------------------
            // AJOUT : fermer MediaMonitor.UI.exe
            // ------------------------------------------------------------
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

