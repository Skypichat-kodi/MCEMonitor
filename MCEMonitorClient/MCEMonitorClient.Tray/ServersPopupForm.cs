using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MCEMonitor.Languages;
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
        private ToolTip _rowTooltip = null!;
        private Label _lblHint = null!;

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
            Text = LanguageManager.Get("Serveurs surveillés") ?? "Serveurs surveillés";
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
                OwnerDraw = true,
                MultiSelect = false
            };

            _list.Columns.Add(LanguageManager.Get("Serveur") ?? "Serveur", 0);
            _list.Columns.Add(LanguageManager.Get("Type") ?? "Type", 130);
            _list.Columns.Add(LanguageManager.Get("État") ?? "État", 80);

            _list.DrawColumnHeader += List_DrawColumnHeader;
            _list.DrawItem += List_DrawItem;
            _list.DrawSubItem += List_DrawSubItem;

            _list.DoubleClick += (s, e) => OpenSelectedUrl();
            _list.MouseMove += List_MouseMove;
            _list.MouseLeave += (s, e) => _list.Cursor = Cursors.Default;

            Controls.Add(_list);

            typeof(Control)
                .GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_list, true);

            // --- ToolTip pour indiquer que les lignes sont cliquables ---
            _rowTooltip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 400,
                ReshowDelay = 200,
                ShowAlways = true
            };

            // --- Bouton Actualiser ---
            _btnRefresh = new Button
            {
                Text = LanguageManager.Get("Actualiser") ?? "Actualiser",
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

            // --- Label d'astuce (dans l'espace entre la liste et les boutons) ---
            _lblHint = new Label
            {
                Location = new Point(15, ClientSize.Height - 85),
                Width = ClientSize.Width - 30,
                Height = 20,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.FromArgb(0x88, 0x88, 0x88),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = LanguageManager.Get("HintDoubleClickOpen")
                    ?? "💡  Astuce : double-cliquez sur une ligne pour ouvrir la page web du serveur."
            };
            Controls.Add(_lblHint);

            // --- Bouton Fermer ---
            _btnClose = new Button
            {
                Text = LanguageManager.Get("Fermer") ?? "Fermer",
                Location = new Point(ClientSize.Width - 135, ClientSize.Height - 50),
                Size = new Size(120, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                BackColor = Color.FromArgb(0x2D, 0x2D, 0x30),
                ForeColor = Color.FromArgb(0xE5, 0xE5, 0xE5),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnClose.FlatAppearance.BorderColor = Color.FromArgb(0x3C, 0x3C, 0x3C);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnClose);

            _list.Resize += (s, e) => LayoutColumns();
            LayoutColumns();

            CancelButton = _btnClose;
        }

        // ---------------------------------------------
        //  Curseur "main" + tooltip sur les lignes
        // ---------------------------------------------
        private void List_MouseMove(object? sender, MouseEventArgs e)
        {
            var hit = _list.HitTest(e.Location);

            if (hit.Item != null)
            {
                _list.Cursor = Cursors.Hand;

                string url = hit.Item.SubItems[0].Tag as string ?? "";
                string currentTooltip = _rowTooltip.GetToolTip(_list);

                string hint = string.IsNullOrWhiteSpace(url)
                    ? (LanguageManager.Get("Aucune URL configurée") ?? "Aucune URL configurée")
                    : string.Format(
                        LanguageManager.Get("Double-cliquez pour ouvrir : {0}")
                            ?? "Double-cliquez pour ouvrir : {0}",
                        url);

                // Évite de réinitialiser le tooltip en boucle (ce qui le ferait clignoter)
                if (currentTooltip != hint)
                    _rowTooltip.SetToolTip(_list, hint);
            }
            else
            {
                _list.Cursor = Cursors.Default;
                _rowTooltip.SetToolTip(_list, "");
            }
        }

        // ---------------------------------------------
        //  Owner Draw : header + rows + dot
        // ---------------------------------------------
        private void List_DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var brush = new SolidBrush(Color.FromArgb(0x2D, 0x2D, 0x30)))
                e.Graphics.FillRectangle(brush, e.Bounds);

            using (var pen = new Pen(Color.FromArgb(0x3C, 0x3C, 0x3C)))
                e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top,
                                         e.Bounds.Right - 1, e.Bounds.Bottom);

            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "",
                new Font("Segoe UI", 9F, FontStyle.Bold),
                e.Bounds,
                Color.FromArgb(0xE5, 0xE5, 0xE5),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private void List_DrawItem(object? sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = false;
        }

        private void List_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
        {
            Color bgColor = e.Item!.Selected
                ? Color.FromArgb(0x4C, 0xC2, 0xFF)
                : (e.ItemIndex % 2 == 0
                    ? Color.FromArgb(0x1F, 0x1F, 0x1F)
                    : Color.FromArgb(0x26, 0x26, 0x26));

            using (var brush = new SolidBrush(bgColor))
                e.Graphics.FillRectangle(brush, e.Bounds);

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
                    e.Graphics.FillEllipse(brush, x, y, dotSize, dotSize);

                return;
            }

            Color textColor = e.Item!.Selected ? Color.Black : Color.FromArgb(0xE5, 0xE5, 0xE5);

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
                    _lblStatus.Text = LanguageManager.Get("Service non joignable")
                        ?? "Service non joignable";
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
                        "",
                    });

                    item.Tag = server.Online
                        ? (string.IsNullOrEmpty(server.Status) ? "ok" : server.Status)
                        : "offline";

                    item.SubItems[0].Tag = server.BaseUrl;

                    _list.Items.Add(item);
                }

                _list.EndUpdate();

                int online = state.Servers.Count(s => s.Online);
                int total = state.Servers.Count;

                _lblStatus.Text = string.Format(
                    LanguageManager.Get("{0}/{1} serveur(s) en ligne  —  Dernière mise à jour : {2}")
                        ?? "{0}/{1} serveur(s) en ligne  —  Dernière mise à jour : {2}",
                    online, total, DateTime.Now.ToString("HH:mm:ss"));
            }
            catch (Exception ex)
            {
                _lblStatus.Text = (LanguageManager.Get("Erreur : ") ?? "Erreur : ") + ex.Message;
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
                _btnRefresh.Text = LanguageManager.Get("Actualisation...") ?? "Actualisation...";
                _lblStatus.Text = LanguageManager.Get("Scan en cours...") ?? "Scan en cours...";

                await ServiceIpcClient.ForceScanAsync();
                await Task.Delay(1500);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _lblStatus.Text = (LanguageManager.Get("Erreur : ") ?? "Erreur : ") + ex.Message;
            }
            finally
            {
                _btnRefresh.Enabled = true;
                _btnRefresh.Text = LanguageManager.Get("Actualiser") ?? "Actualiser";
            }
        }

        // ---------------------------------------------
        //  Double-clic sur une ligne → ouvre l'URL
        // ---------------------------------------------
        private void OpenSelectedUrl()
        {
            if (_list.SelectedItems.Count == 0) return;

            string? url = _list.SelectedItems[0].SubItems[0].Tag as string;

            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                // Ajoute le paramètre de langue (comme le font les UI MediaMonitor, RomMonitor et SystemMonitor)
                string lang = LanguageManager.CurrentLanguage ?? "fr-FR";
                string separator = url.Contains('?') ? "&" : "?";
                string urlWithLang = $"{url}{separator}lang={lang}";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = urlWithLang,
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

            int widthServer = totalWidth - widthType - widthState;
            if (widthServer < 100) widthServer = 100;

            _list.Columns[0].Width = widthServer;
            _list.Columns[1].Width = widthType;
            _list.Columns[2].Width = widthState;
        }
    }
}