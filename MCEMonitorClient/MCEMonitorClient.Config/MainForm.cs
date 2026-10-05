using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MCEMonitorClient.Config.Models;
using MCEMonitorClient.Config.Services;

namespace MCEMonitorClient.Config
{
    public class MainForm : Form
    {
        private ServersConfig _config;
        private readonly Dictionary<string, string> _serverStates = new();
        private System.Windows.Forms.Timer _stateTimer = null!;
        private System.Windows.Forms.Timer _serviceWatchdog = null!;

        // Contrôles
        private DataGridView _grid = null!;
        private Button _btnAdd = null!;
        private Button _btnEdit = null!;
        private Button _btnDelete = null!;
        private Button _btnAutoService = null!;      // ? Automatique ON/OFF (tâche planifiée)
        private Button _btnServiceOnOff = null!;     // ? ON/OFF (démarrage manuel)
        private Label _lblServiceStatus = null!;     // ? Label descriptif à droite
        private NumericUpDown _numInterval = null!;
        private CheckBox _chkNotify = null!;
        private CheckBox _chkSound = null!;
        private Button _btnSave = null!;
        private Label _lblStatus = null!;

        public MainForm()
        {
            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            }
            catch { }

            _config = ServerConfigStore.Load();

            InitializeUI();
            RefreshGrid();
            LoadOptions();

            // --- Polling d'état des serveurs ---
            _ = RefreshAllStatesAsync();

            _stateTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            _stateTimer.Tick += async (s, e) => await RefreshAllStatesAsync();
            _stateTimer.Start();

            // --- Watchdog : met à jour l'état du service ---
            _serviceWatchdog = new System.Windows.Forms.Timer { Interval = 2000 };
            _serviceWatchdog.Tick += (s, e) => UpdateServiceStatus();
            _serviceWatchdog.Start();

            UpdateServiceStatus();
        }

        // ---------------------------------------------
        //  Construction de l'UI
        // ---------------------------------------------
        private void InitializeUI()
        {
            Text = "MCEMonitorClient - Configuration";
            Size = new Size(900, 680);
            MinimumSize = new Size(800, 630);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font(Theme.FontFamily, 9F);

            // --- Titre Serveurs ---
            var lblTitle = new Label
            {
                Text = "Serveurs surveillés",
                Location = new Point(20, 15),
                Width = 400,
                Height = 24,
                Font = new Font(Theme.FontFamily, 12F, FontStyle.Bold),
                ForeColor = Theme.Accent
            };
            Controls.Add(lblTitle);

            // --- DataGridView ---
            _grid = new DataGridView
            {
                Location = new Point(20, 50),
                Size = new Size(ClientSize.Width - 40, 250),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackgroundColor = Theme.Panel,
                BorderStyle = BorderStyle.FixedSingle,
                GridColor = Theme.Border,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 32,
                RowTemplate = { Height = 28 }
            };

            _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Panel;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font(Theme.FontFamily, 9F, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Panel;

            _grid.DefaultCellStyle.BackColor = Theme.RowOdd;
            _grid.DefaultCellStyle.ForeColor = Theme.Text;
            _grid.DefaultCellStyle.SelectionBackColor = Theme.Accent;
            _grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            _grid.DefaultCellStyle.Font = new Font(Theme.FontFamily, 9F);

            _grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.RowEven;
            _grid.AlternatingRowsDefaultCellStyle.ForeColor = Theme.Text;
            _grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Theme.Accent;
            _grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.Black;

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Nom", FillWeight = 200 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Url", HeaderText = "URL", FillWeight = 220 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", FillWeight = 100 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "User", HeaderText = "Login", FillWeight = 80 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Enabled", HeaderText = "Actif", FillWeight = 60 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "État", FillWeight = 60 });

            _grid.CellPainting += Grid_CellPainting;
            _grid.DoubleClick += (s, e) => EditSelected();

            Controls.Add(_grid);

            // --- Boutons d'action serveurs ---
            int buttonY = 315;

            _btnAdd = CreateButton("Ajouter", 20, buttonY, 120, Theme.Accent, Color.Black, true);
            _btnAdd.Click += (s, e) => AddServer();

            _btnEdit = CreateButton("Modifier", 150, buttonY, 120, Theme.Panel, Theme.Text, false);
            _btnEdit.Click += (s, e) => EditSelected();

            _btnDelete = CreateButton("Supprimer", 280, buttonY, 120, Theme.Panel, Theme.Text, false);
            _btnDelete.Click += (s, e) => DeleteSelected();

            Controls.Add(_btnAdd);
            Controls.Add(_btnEdit);
            Controls.Add(_btnDelete);

            // --- Section Service ---
            var lblService = new Label
            {
                Text = "Service de surveillance",
                Location = new Point(20, 365),
                Width = 400,
                Height = 24,
                Font = new Font(Theme.FontFamily, 12F, FontStyle.Bold),
                ForeColor = Theme.Accent
            };
            Controls.Add(lblService);

            // Bouton 1 : Automatique ON/OFF (tâche planifiée)
            _btnAutoService = CreateButton("Automatique OFF", 20, 400, 220,
                Color.FromArgb(220, 60, 60), Color.White, true);
            _btnAutoService.Click += (s, e) => ToggleAutomaticTask();
            Controls.Add(_btnAutoService);

            // Bouton 2 : ON/OFF (démarrage manuel du service)
            _btnServiceOnOff = CreateButton("OFF", 250, 400, 80,
                Color.FromArgb(220, 60, 60), Color.White, true);
            _btnServiceOnOff.Click += (s, e) => ToggleServiceManual();
            Controls.Add(_btnServiceOnOff);

            // Label d'état
            _lblServiceStatus = new Label
            {
                Text = "? Service arrêté",
                Location = new Point(340, 407),
                Width = 500,
                ForeColor = Theme.TextDim
            };
            Controls.Add(_lblServiceStatus);

            // --- Section Options ---
            var lblOptions = new Label
            {
                Text = "Options",
                Location = new Point(20, 455),
                Width = 400,
                Height = 24,
                Font = new Font(Theme.FontFamily, 12F, FontStyle.Bold),
                ForeColor = Theme.Accent
            };
            Controls.Add(lblOptions);

            var lblInterval = new Label
            {
                Text = "Intervalle de polling :",
                Location = new Point(20, 495),
                Width = 180,
                ForeColor = Theme.TextDim
            };
            Controls.Add(lblInterval);

            _numInterval = new NumericUpDown
            {
                Location = new Point(210, 492),
                Width = 80,
                Minimum = 5,
                Maximum = 600,
                Value = 30,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_numInterval);

            var lblSeconds = new Label
            {
                Text = "secondes",
                Location = new Point(295, 495),
                Width = 100,
                ForeColor = Theme.TextDim
            };
            Controls.Add(lblSeconds);

            _chkNotify = CreateCheckBox("Notifier les changements d'état", 20, 530);
            _chkSound = CreateCheckBox("Jouer un son sur alerte critique", 20, 555);

            Controls.Add(_chkNotify);
            Controls.Add(_chkSound);

            // --- Bouton Enregistrer ---
            _btnSave = CreateButton(
                "Enregistrer",
                ClientSize.Width - 170,
                ClientSize.Height - 60,
                150,
                Theme.Accent, Color.Black, true);
            _btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnSave.Click += (s, e) => SaveAll();
            Controls.Add(_btnSave);

            // --- Barre de statut ---
            _lblStatus = new Label
            {
                Location = new Point(20, ClientSize.Height - 40),
                Width = ClientSize.Width - 200,
                Height = 20,
                ForeColor = Theme.TextDim,
                Text = $"Fichier : {ServerConfigStore.GetConfigPath()}",
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(_lblStatus);
        }

        private Button CreateButton(string text, int x, int y, int w,
                                     Color back, Color fore, bool bold,
                                     int height = 32)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Width = w,
                Height = height,
                BackColor = back,
                ForeColor = fore,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font(Theme.FontFamily, 9F,
                    bold ? FontStyle.Bold : FontStyle.Regular)
            };
            btn.FlatAppearance.BorderSize = bold ? 0 : 1;
            btn.FlatAppearance.BorderColor = Theme.Border;
            return btn;
        }

        private CheckBox CreateCheckBox(string text, int x, int y)
        {
            return new CheckBox
            {
                Text = text,
                Location = new Point(x, y),
                Width = 400,
                ForeColor = Theme.Text
            };
        }

        // ---------------------------------------------
        //  Remplissage du DataGridView
        // ---------------------------------------------
        private void RefreshGrid()
        {
            _grid.Rows.Clear();

            foreach (var server in _config.Servers)
            {
                _grid.Rows.Add(
                    server.Name,
                    server.FullUrl,
                    server.ServiceType,
                    server.Username,
                    server.Enabled ? "Oui" : "Non",
                    ""
                );

                _grid.Rows[_grid.Rows.Count - 1].Tag = server.Id;
            }
        }

        private void LoadOptions()
        {
            _numInterval.Value = Math.Clamp(_config.PollIntervalSeconds,
                                            (int)_numInterval.Minimum,
                                            (int)_numInterval.Maximum);
            _chkNotify.Checked = _config.NotifyOnStateChange;
            _chkSound.Checked = _config.PlaySoundOnCritical;
        }

        // ---------------------------------------------
        //  Paint custom : pastille d'état
        // ---------------------------------------------
        private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            if (_grid.Columns[e.ColumnIndex].Name != "State")
                return;

            e.PaintBackground(e.CellBounds, true);

            var server = GetServerFromRow(e.RowIndex);
            string state = "offline";
            if (server != null && _serverStates.TryGetValue(server.Id, out var s))
                state = s;

            Color color = (server?.Enabled != true)
                ? Color.FromArgb(80, 80, 80)
                : state switch
                {
                    "ok"       => Color.FromArgb(0x6C, 0xCB, 0x5F),
                    "warning"  => Color.FromArgb(0xFF, 0xB9, 0x00),
                    "critical" => Color.FromArgb(0xFF, 0x63, 0x47),
                    _          => Color.FromArgb(0x88, 0x88, 0x88)
                };

            int dotSize = 12;
            int x = e.CellBounds.Left + (e.CellBounds.Width - dotSize) / 2;
            int y = e.CellBounds.Top + (e.CellBounds.Height - dotSize) / 2;

            using (var brush = new SolidBrush(color))
            {
                e.Graphics!.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.FillEllipse(brush, x, y, dotSize, dotSize);
            }

            e.Handled = true;
        }

        private ServerEntry? GetServerFromRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count)
                return null;

            string? id = _grid.Rows[rowIndex].Tag as string;
            if (string.IsNullOrEmpty(id))
                return null;

            return _config.Servers.FirstOrDefault(s => s.Id == id);
        }

        private ServerEntry? GetSelectedServer()
        {
            if (_grid.SelectedRows.Count == 0)
                return null;

            return GetServerFromRow(_grid.SelectedRows[0].Index);
        }

        // ---------------------------------------------
        //  Actions Ajouter / Modifier / Supprimer
        // ---------------------------------------------
        private void AddServer()
        {
            using var dlg = new ServerEditForm();
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _config.Servers.Add(dlg.Result);
                RefreshGrid();
                _ = RefreshAllStatesAsync();
            }
        }

        private void EditSelected()
        {
            var server = GetSelectedServer();
            if (server == null)
            {
                MessageBox.Show("Sélectionnez un serveur.", "Info",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new ServerEditForm(server);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                RefreshGrid();
                _ = RefreshAllStatesAsync();
            }
        }

        private void DeleteSelected()
        {
            var server = GetSelectedServer();
            if (server == null)
            {
                MessageBox.Show("Sélectionnez un serveur.", "Info",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Voulez-vous vraiment supprimer le serveur :\n\n{server.Name} ?",
                "Confirmation",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;

            _config.Servers.Remove(server);
            _serverStates.Remove(server.Id);
            RefreshGrid();
        }

        // ---------------------------------------------
        //  Sauvegarde globale
        // ---------------------------------------------
        private void SaveAll()
        {
            _config.PollIntervalSeconds = (int)_numInterval.Value;
            _config.NotifyOnStateChange = _chkNotify.Checked;
            _config.PlaySoundOnCritical = _chkSound.Checked;

            bool ok = ServerConfigStore.Save(_config);

            if (ok)
            {
                MessageBox.Show("Configuration enregistrée.", "OK",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                _ = RefreshAllStatesAsync();
            }
            else
            {
                MessageBox.Show("Erreur lors de la sauvegarde.", "Erreur",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------------------------------------------
        //  Test de CONNEXION aux serveurs (pas de défaillances)
        // ---------------------------------------------
        private async Task RefreshAllStatesAsync()
        {
            var config = ServerConfigStore.Load();
            var servers = config.Servers.Where(s => s.Enabled).ToList();

            foreach (var server in servers)
            {
                try
                {
                    var handler = new System.Net.Http.HttpClientHandler
                    {
                        Credentials = new System.Net.NetworkCredential(server.Username, server.Password),
                        PreAuthenticate = true
                    };

                    using var http = new System.Net.Http.HttpClient(handler)
                    {
                        Timeout = TimeSpan.FromSeconds(5)
                    };

                    using var resp = await http.GetAsync(server.ApiSummaryUrl);

                    // ? Test de connexion uniquement : OK si le serveur répond (200)
                    _serverStates[server.Id] = resp.IsSuccessStatusCode ? "ok" : "offline";
                }
                catch
                {
                    _serverStates[server.Id] = "offline";
                }
            }

            if (InvokeRequired)
                Invoke(new Action(() => _grid.Invalidate()));
            else
                _grid.Invalidate();
        }

        // ---------------------------------------------
        //  SERVICE : état, ON/OFF manuel, tâche auto
        // ---------------------------------------------
        private bool IsServiceRunning()
        {
            return System.Diagnostics.Process.GetProcessesByName("MCEMonitorClient.Service").Length > 0;
        }

        private void UpdateServiceStatus()
        {
            bool running = IsServiceRunning();
            bool taskExists = TaskSchedulerHelper.ClientServiceTaskExists();

            // --- Bouton 1 : Automatique ON/OFF (tâche planifiée) ---
            _btnAutoService.Text = taskExists ? "Automatique ON" : "Automatique OFF";
            _btnAutoService.BackColor = taskExists
                ? Color.FromArgb(76, 175, 80)
                : Color.FromArgb(220, 60, 60);

            // --- Bouton 2 : ON/OFF (service en cours) ---
            _btnServiceOnOff.Text = running ? "ON" : "OFF";
            _btnServiceOnOff.BackColor = running
                ? Color.FromArgb(76, 175, 80)
                : Color.FromArgb(220, 60, 60);

            // --- Label d'état ---
            string status = running ? "Service en cours" : "Service arrêté";

            if (taskExists)
                status += " — Auto au démarrage";
            else
                status += " — Pas de démarrage auto";

            _lblServiceStatus.Text = status;
            _lblServiceStatus.ForeColor = running ? Theme.Ok : Theme.TextDim;
        }

        // --- Bouton 1 : Automatique (tâche planifiée) ---
        private void ToggleAutomaticTask()
        {
            try
            {
                if (TaskSchedulerHelper.ClientServiceTaskExists())
                {
                    var result = MessageBox.Show(
                        "Voulez-vous vraiment désactiver le démarrage automatique ?\n" +
                        "Le service ne sera plus lancé au démarrage de Windows.",
                        "Confirmation",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result != DialogResult.Yes)
                        return;

                    TaskSchedulerHelper.DeleteClientServiceTask();
                    MessageBox.Show("Démarrage automatique désactivé.", "OK",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    var result = MessageBox.Show(
                        "Voulez-vous activer le démarrage automatique du service ?\n" +
                        "Le service sera lancé au démarrage de Windows.",
                        "Confirmation",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result != DialogResult.Yes)
                        return;

                    TaskSchedulerHelper.CreateClientServiceTask();

                    // Vérification silencieuse
                    if (!TaskSchedulerHelper.ClientServiceTaskExists())
                    {
                        MessageBox.Show("Erreur : la tâche n'a pas pu être créée.",
                            "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    MessageBox.Show("Démarrage automatique activé.", "OK",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                UpdateServiceStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur : " + ex.Message);
            }
        }

        // --- Bouton 2 : ON/OFF manuel ---
        private void ToggleServiceManual()
        {
            if (IsServiceRunning())
                StopService();
            else
                StartService();
        }

        private void StartService()
        {
            try
            {
                string servicePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MCEMonitor",
                    "MCEMonitorClient.Service.exe");

                if (!System.IO.File.Exists(servicePath))
                {
                    MessageBox.Show(
                        $"Impossible de trouver :\n{servicePath}",
                        "Erreur",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = servicePath,
                    UseShellExecute = true
                });

                System.Threading.Thread.Sleep(1200);
                UpdateServiceStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur au démarrage du service : " + ex.Message);
            }
        }

        private void StopService()
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("MCEMonitorClient.Service"))
                {
                    try { p.Kill(); } catch { }
                }

                System.Threading.Thread.Sleep(500);
                UpdateServiceStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur à l'arrêt du service : " + ex.Message);
            }
        }
    }
}