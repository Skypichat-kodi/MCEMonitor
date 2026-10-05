using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MCEMonitorClient.Tray.Ipc;

namespace MCEMonitorClient.Tray
{
    public class ServersPopupForm : Form
    {
        private ListView _list = null!;
        private Button _btnRefresh = null!;
        private Button _btnClose = null!;
        private System.Windows.Forms.Timer _refreshTimer = null!;

        public ServersPopupForm()
        {
            InitializeUI();
            _ = RefreshAsync();

            _refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _refreshTimer.Tick += async (s, e) => await RefreshAsync();
            _refreshTimer.Start();
        }

        private void InitializeUI()
        {
            Text = "Serveurs surveillés";
            Size = new Size(700, 400);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(0x1E, 0x1E, 0x1E);
            ForeColor = Color.FromArgb(0xE5, 0xE5, 0xE5);
            Font = new Font("Segoe UI", 9F);

            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                this.Icon = Icon.ExtractAssociatedIcon(exePath);
            }
            catch { }

            _list = new ListView
            {
                Location = new Point(15, 15),
                Size = new Size(ClientSize.Width - 30, ClientSize.Height - 80),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = Color.FromArgb(0x25, 0x25, 0x26),
                ForeColor = Color.FromArgb(0xE5, 0xE5, 0xE5),
                BorderStyle = BorderStyle.FixedSingle,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };

            _list.Columns.Add("Serveur", 250);
            _list.Columns.Add("Type", 120);
            _list.Columns.Add("État", 100);
            _list.Columns.Add("Problème", 200);

            _list.DoubleClick += (s, e) => OpenSelectedUrl();

            Controls.Add(_list);

            _btnRefresh = new Button
            {
                Text = "Rafraîchir",
                Location = new Point(15, ClientSize.Height - 50),
                Size = new Size(120, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                BackColor = Color.FromArgb(0x4C, 0xC2, 0xFF),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _btnRefresh.FlatAppearance.BorderSize = 0;
            _btnRefresh.Click += async (s, e) => await RefreshAsync();
            Controls.Add(_btnRefresh);

            _btnClose = new Button
            {
                Text = "Fermer",
                Location = new Point(ClientSize.Width - 135, ClientSize.Height - 50),
                Size = new Size(120, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                BackColor = Color.FromArgb(0x2D, 0x2D, 0x30),
                ForeColor = Color.FromArgb(0xE5, 0xE5, 0xE5),
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.OK
            };
            _btnClose.FlatAppearance.BorderColor = Color.FromArgb(0x3C, 0x3C, 0x3C);
            Controls.Add(_btnClose);

            AcceptButton = _btnRefresh;
            CancelButton = _btnClose;
        }

        private async Task RefreshAsync()
        {
            try
            {
                var state = await ServiceIpcClient.GetStateAsync();
                if (state == null) return;

                _list.Items.Clear();

                foreach (var server in state.Servers.OrderByDescending(s => s.Status == "critical"))
                {
                    string statusLabel = server.Online
                        ? (server.Status == "critical" ? "CRITIQUE"
                         : server.Status == "warning" ? "Alerte"
                         : "OK")
                        : "Hors ligne";

                    var item = new ListViewItem(new[]
                    {
                        server.ServerName,
                        server.ServiceType,
                        statusLabel,
                        server.FirstProblem
                    });

                    item.Tag = server.BaseUrl;

                    item.ForeColor = server.Status switch
                    {
                        "critical" => Color.FromArgb(0xFF, 0x63, 0x47),
                        "warning"  => Color.FromArgb(0xFF, 0xB9, 0x00),
                        "offline"  => Color.FromArgb(0x88, 0x88, 0x88),
                        _          => Color.FromArgb(0x6C, 0xCB, 0x5F)
                    };

                    _list.Items.Add(item);
                }
            }
            catch { }
        }

        private void OpenSelectedUrl()
        {
            if (_list.SelectedItems.Count == 0) return;

            string? url = _list.SelectedItems[0].Tag as string;

            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _refreshTimer?.Stop();
            _refreshTimer?.Dispose();
            base.OnFormClosing(e);
        }
    }
}