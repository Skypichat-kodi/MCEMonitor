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
        private Label _lblStatus = null!;
        private System.Windows.Forms.Timer _refreshTimer = null!;

        private const int COL_STATUS_INDEX = 2;   // Index de la colonne "État"

        public ServersPopupForm()
        {
            InitializeUI();
            _ = RefreshAsync();

            // Auto-refresh toutes les 10s
            _refreshTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            _refreshTimer.Tick += async (s, e) => await RefreshAsync();
            _refreshTimer.Start();
        }

        // ---------------------------------------------
        //  Construction de l'UI
        // ---------------------------------------------
        private void InitializeUI()
        {
            Text = "Serveurs surveillés";
            Size = new Size(720, 420);
            MinimumSize = new Size(500, 300);
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

            // --- ListView avec OwnerDraw (pour dessiner les dots) ---
            _list = new ListView
            {
                Location = new Point(15, 15),
                Size = new Size(ClientSize.Width - 30, ClientSize.Height - 110),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = Color.FromArgb(0x25, 0x25, 0x26),
                ForeColor = Color.FromArgb(0xE5, 0xE5, 0xE5),
                BorderStyle = BorderStyle.FixedSingle,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                OwnerDraw = true,           // ? Pour dessiner nous-mêmes
                MultiSelect = false
            };

            _list.Columns.Add("Serveur", 0);   // largeur calculée dans LayoutColumns()
            _list.Columns.Add("Type", 130);    // largeur fixe
            _list.Columns.Add("État", 80);     // largeur fixe

            _list.DrawColumnHeader += List_DrawColumnHeader;
            _list.DrawItem += List_DrawItem;
            _list.DrawSubItem += List_DrawSubItem;

            _list.DoubleClick += (s, e) => OpenSelectedUrl();

            Controls.Add(_list);

            typeof(Control)
                .GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_list, true);
    
            // --- Bouton Actualiser ---
            _btnRefresh = new Button
            {
                Text = "Actualiser",
                Location = new Point(15, ClientSize.Height - 50),
                Size = new Size(150, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                BackColor = Color.FromArgb(0x4C, 0xC2, 0xFF),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnRefresh.FlatAppearance.BorderSize = 0;
            _btnRefresh.Click += async (s, e) => await ForceRefreshAsync();
            Controls.Add(_btnRefresh);

            // --- Label de statut ---
            _lblStatus = new Label
            {
                Location = new Point(180, ClientSize.Height - 45),
                Width = ClientSize.Width - 340,
                Height = 20,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.FromArgb(0x88, 0x88, 0x88),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = ""
            };
            Controls.Add(_lblStatus);

            // --- Bouton Fermer ---
            _btnClose = new Button
            {
                Text = "Fermer",
                Location = new Point(ClientSize.Width - 135, ClientSize.Height - 50),
                Size = new Size(120, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                BackColor = Color.FromArgb(0x2D, 0x2D, 0x30),
                ForeColor = Color.FromArgb(0xE5, 0xE5, 0xE5),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnClose.FlatAppearance.BorderColor = Color.FromArgb(0x3C, 0x3C, 0x3C);
            _btnClose.Click += (s, e) => Close();   // ? Utilise Close() au lieu de DialogResult
            Controls.Add(_btnClose);

            _list.Resize += (s, e) => LayoutColumns();
            LayoutColumns();
            
            CancelButton = _btnClose;
        }

        // ---------------------------------------------
        //  Owner Draw : header + rows + dot
        // ---------------------------------------------
        private void List_DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
        {
            // Fond du header
            using (var brush = new SolidBrush(Color.FromArgb(0x2D, 0x2D, 0x30)))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            // Bordure droite
            using (var pen = new Pen(Color.FromArgb(0x3C, 0x3C, 0x3C)))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top,
                                          e.Bounds.Right - 1, e.Bounds.Bottom);
            }

            // Texte
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "",
                new Font("Segoe UI", 9F, FontStyle.Bold),
                e.Bounds,
                Color.FromArgb(0xE5, 0xE5, 0xE5),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private void List_DrawItem(object? sender, DrawListViewItemEventArgs e)
        {
            // Le fond est dessiné dans DrawSubItem — rien à faire ici.
            e.DrawDefault = false;
        }

        private void List_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
        {
            // ---------------------------------------------------------
            // 1. TOUJOURS remplir le fond du subitem (fix du hover)
            // ---------------------------------------------------------
            Color bgColor = e.Item!.Selected
                ? Color.FromArgb(0x4C, 0xC2, 0xFF)                       // bleu sélection
                : (e.ItemIndex % 2 == 0
                    ? Color.FromArgb(0x1F, 0x1F, 0x1F)                   // ligne paire
                    : Color.FromArgb(0x26, 0x26, 0x26));                 // ligne impaire

            using (var brush = new SolidBrush(bgColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            // ---------------------------------------------------------
            // 2. Colonne État : on dessine un DOT
            // ---------------------------------------------------------
            if (e.ColumnIndex == COL_STATUS_INDEX)
            {
                string state = e.Item?.Tag as string ?? "offline";

                Color color = state switch
                {
                    "ok"       => Color.FromArgb(0x6C, 0xCB, 0x5F),
                    "warning"  => Color.FromArgb(0xFF, 0xB9, 0x00),
                    "critical" => Color.FromArgb(0xFF, 0x63, 0x47),
                    _          => Color.FromArgb(0x88, 0x88, 0x88)
                };

                int dotSize = 14;
                int x = e.Bounds.Left + (e.Bounds.Width - dotSize) / 2;
                int y = e.Bounds.Top + (e.Bounds.Height - dotSize) / 2;

                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                using (var brush = new SolidBrush(color))
                {
                    e.Graphics.FillEllipse(brush, x, y, dotSize, dotSize);
                }

                return;
            }

            // ---------------------------------------------------------
            // 3. Autres colonnes : texte normal
            // ---------------------------------------------------------
            Color textColor = e.Item!.Selected
                ? Color.Black
                : Color.FromArgb(0xE5, 0xE5, 0xE5);

            TextRenderer.DrawText(e.Graphics, e.SubItem!.Text,
                new Font("Segoe UI", 9F),
                e.Bounds,
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        // ---------------------------------------------
        //  Rafraîchissement (pull)
        // ---------------------------------------------
        private async Task RefreshAsync()
        {
            try
            {
                var state = await ServiceIpcClient.GetStateAsync();
                if (state == null)
                {
                    _lblStatus.Text = "Service non joignable";
                    return;
                }

                _list.BeginUpdate();
                _list.Items.Clear();

                foreach (var server in state.Servers
                    .OrderByDescending(s => s.Status == "critical")
                    .ThenByDescending(s => s.Status == "warning")
                    .ThenByDescending(s => !s.Online))
                {
                    var item = new ListViewItem(new[]
                    {
                        server.ServerName,
                        server.ServiceType,
                        "",   // colonne État vide (remplie par DrawSubItem)
                    });

                    // ? Tag = état du serveur (utilisé par DrawSubItem pour la couleur du dot)
                    item.Tag = server.Online
                        ? (string.IsNullOrEmpty(server.Status) ? "ok" : server.Status)
                        : "offline";

                    // Tag secondaire pour l'URL (sur le sub-item)
                    item.SubItems[0].Tag = server.BaseUrl;

                    _list.Items.Add(item);
                }

                _list.EndUpdate();

                // Résumé dans le label
                int online = state.Servers.Count(s => s.Online);
                int total = state.Servers.Count;

                _lblStatus.Text = $"{online}/{total} serveur(s) en ligne  —  Dernière màj : {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Erreur : " + ex.Message;
            }
        }

        // ---------------------------------------------
        //  Rafraîchissement forcé (force-scan côté Service)
        // ---------------------------------------------
        private async Task ForceRefreshAsync()
        {
            try
            {
                _btnRefresh.Enabled = false;
                _btnRefresh.Text = "Actualisation...";
                _lblStatus.Text = "Scan en cours...";

                // ? Demande au Service de rescan immédiatement
                await ServiceIpcClient.ForceScanAsync();

                // Petite pause pour laisser le Service finir son poll
                await Task.Delay(1500);

                // Puis on rafraîchit l'affichage
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Erreur : " + ex.Message;
            }
            finally
            {
                _btnRefresh.Enabled = true;
                _btnRefresh.Text = "Actualiser";
            }
        }

        // ---------------------------------------------
        //  Double-clic sur une ligne ? ouvre l'URL
        // ---------------------------------------------
        private void OpenSelectedUrl()
        {
            if (_list.SelectedItems.Count == 0) return;

            string? url = _list.SelectedItems[0].SubItems[0].Tag as string;

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
        
        private void LayoutColumns()
        {
            if (_list.Columns.Count < 3) return;

            const int widthType  = 130;
            const int widthState = 80;

            int totalWidth = _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            if (totalWidth <= 0) return;

            // Serveur = tout ce qui reste, avec un minimum raisonnable
            int widthServer = totalWidth - widthType - widthState;
            if (widthServer < 100) widthServer = 100;

            _list.Columns[0].Width = widthServer;
            _list.Columns[1].Width = widthType;
            _list.Columns[2].Width = widthState;
        }        
    }
}