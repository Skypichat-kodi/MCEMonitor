using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MCEMonitorClient.Tray
{
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon _trayIcon;

        public TrayApplicationContext()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "MCEMonitorClient"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Ouvrir la configuration", null, (s, e) => OpenConfig());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quitter", null, (s, e) => Exit());
            _trayIcon.ContextMenuStrip = menu;
        }

        private void OpenConfig()
        {
            // TODO : lancer MCEMonitorClient.Config.exe
        }

        private void Exit()
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Exit();
        }
    }
}