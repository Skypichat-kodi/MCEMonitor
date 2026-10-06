using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MCEMonitor.Languages;
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
        private Button _btnAutoService = null!;      // Automatique ON/OFF (tâche planifiée)
        private Button _btnServiceOnOff = null!;     // ON/OFF (démarrage manuel)
        private Label _lblServiceStatus = null!;     // Label descriptif à droite
        private NumericUpDown _numInterval = null!;
        private CheckBox _chkNotify = null!;
        private CheckBox _chkSound = null!;
        private Button _btnSave = null!;
        private Label _lblStatus = null!;
        private PictureBox _icoService = null!;
        private PictureBox _icoTray = null!;

        // Icônes d'état
        private Image? _imgOk;
        private Image? _imgKo;

        public MainForm()
        {
            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            }
            catch { }

            // Charge les icônes d'état (une seule fois)
            _imgOk = LoadStateIcon("check.png");
            _imgKo = LoadStateIcon("critical.png");

            _config = ServerConfigStore.Load();

            InitializeUI();
            RefreshGrid();
            LoadOptions();

            // Vérifie/crée la tâche Tray au démarrage (comme MCEMonitor)
            EnsureTrayTaskExists();

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
        //  Création automatique de la tâche Tray
        // ---------------------------------------------
        private void EnsureTrayTaskExists()
        {
            try
            {
                if (ServiceInstaller.TrayTaskExists())
                    return;

                ServiceInstaller.CreateTrayTask();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("EnsureTrayTaskExists : " + ex.Message);
            }
        }

        // ---------------------------------------------
        //  Construction de l'UI
        // ---------------------------------------------
        private void InitializeUI()
        {
            Text = LanguageManager.Get("MCEMonitorClient - Configuration") ?? "MCEMonitorClient - Configuration";
            Size = new Size(900, 680);
            MinimumSize = new Size(800, 630);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font(Theme.FontFamily, 9F);

            // --- Titre Serveurs ---
            var lblTitle = new Label
            {
                Text = LanguageManager.Get("Serveurs surveillés") ?? "Serveurs surveillés",
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

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name",    HeaderText = LanguageManager.Get("Nom")   ?? "Nom",   FillWeight = 200 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Url",     HeaderText = LanguageManager.Get("URL")   ?? "URL",   FillWeight = 220 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type",    HeaderText = LanguageManager.Get("Type")  ?? "Type",  FillWeight = 100 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "User",    HeaderText = LanguageManager.Get("Login") ?? "Login", FillWeight = 80 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Enabled", HeaderText = LanguageManager.Get("Actif") ?? "Actif", FillWeight = 60 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State",   HeaderText = LanguageManager.Get("État")  ?? "État",  FillWeight = 60 });

            _grid.CellPainting += Grid_CellPainting;
            _grid.DoubleClick += (s, e) => EditSelected();

            Controls.Add(_grid);

            // --- Boutons d'action serveurs ---
            int buttonY = 315;

            _btnAdd = CreateButton(LanguageManager.Get("Ajouter") ?? "Ajouter", 20, buttonY, 120, Theme.Accent, Color.Black, true);
            _btnAdd.Click += (s, e) => AddServer();

            _btnEdit = CreateButton(LanguageManager.Get("Modifier") ?? "Modifier", 150, buttonY, 120, Theme.Panel, Theme.Text, false);
            _btnEdit.Click += (s, e) => EditSelected();

            _btnDelete = CreateButton(LanguageManager.Get("Supprimer") ?? "Supprimer", 280, buttonY, 120, Theme.Panel, Theme.Text, false);
            _btnDelete.Click += (s, e) => DeleteSelected();

            Controls.Add(_btnAdd);
            Controls.Add(_btnEdit);
            Controls.Add(_btnDelete);

            // --- Section Service ---
            var lblService = new Label
            {
                Text = LanguageManager.Get("Service de surveillance") ?? "Service de surveillance",
                Location = new Point(20, 365),
                Width = 400,
                Height = 24,
                Font = new Font(Theme.FontFamily, 12F, FontStyle.Bold),
                ForeColor = Theme.Accent
            };
            Controls.Add(lblService);

            // Bouton 1 : Automatique ON/OFF (tâche planifiée Service)
            _btnAutoService = CreateButton(LanguageManager.Get("Automatique OFF") ?? "Automatique OFF", 20, 400, 220,
                Color.FromArgb(220, 60, 60), Color.White, true);
            _btnAutoService.Click += (s, e) => ToggleAutomaticTask();
            Controls.Add(_btnAutoService);

            // Bouton 2 : ON/OFF (démarrage manuel du service)
            _btnServiceOnOff = CreateButton(LanguageManager.Get("OFF") ?? "OFF", 250, 400, 80,
                Color.FromArgb(220, 60, 60), Color.White, true);
            _btnServiceOnOff.Click += (s, e) => ToggleServiceManual();
            Controls.Add(_btnServiceOnOff);

            // Icône d'état + label "Service"
            _icoService = new PictureBox
            {
                Location = new Point(340, 407),
                Size = new Size(16, 16),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            Controls.Add(_icoService);

            var lblServiceName = new Label
            {
                Text = LanguageManager.Get("Service") ?? "Service",
                Location = new Point(360, 407),
                Width = 60,
                ForeColor = Theme.TextDim
            };
            Controls.Add(lblServiceName);

            // Icône d'état + label "Tray"
            _icoTray = new PictureBox
            {
                Location = new Point(430, 407),
                Size = new Size(16, 16),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            Controls.Add(_icoTray);

            var lblTrayName = new Label
            {
                Text = LanguageManager.Get("Tray") ?? "Tray",
                Location = new Point(450, 407),
                Width = 60,
                ForeColor = Theme.TextDim
            };
            Controls.Add(lblTrayName);

            // Label texte restant (Auto / pas de démarrage auto)
            _lblServiceStatus = new Label
            {
                Text = "",
                Location = new Point(530, 407),
                Width = ClientSize.Width - 550,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Theme.TextDim
            };
            Controls.Add(_lblServiceStatus);

            // --- Section Options ---
            var lblOptions = new Label
            {
                Text = LanguageManager.Get("Options") ?? "Options",
                Location = new Point(20, 455),
                Width = 400,
                Height = 24,
                Font = new Font(Theme.FontFamily, 12F, FontStyle.Bold),
                ForeColor = Theme.Accent
            };
            Controls.Add(lblOptions);

            var lblInterval = new Label
            {
                Text = LanguageManager.Get("Intervalle de polling :") ?? "Intervalle de polling :",
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
                Text = LanguageManager.Get("secondes") ?? "secondes",
                Location = new Point(295, 495),
                Width = 100,
                ForeColor = Theme.TextDim
            };
            Controls.Add(lblSeconds);

            _chkNotify = CreateCheckBox(LanguageManager.Get("Notifier les changements d'état") ?? "Notifier les changements d'état", 20, 530);
            _chkSound = CreateCheckBox(LanguageManager.Get("Jouer un son sur alerte critique") ?? "Jouer un son sur alerte critique", 20, 555);

            Controls.Add(_chkNotify);
            Controls.Add(_chkSound);

            // --- Bouton Enregistrer ---
            _btnSave = CreateButton(
                LanguageManager.Get("Enregistrer") ?? "Enregistrer",
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
                Text = string.Format(
                    LanguageManager.Get("Fichier : {0}") ?? "Fichier : {0}",
                    ServerConfigStore.GetConfigPath()),
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
                    server.Enabled
                        ? (LanguageManager.Get("Oui") ?? "Oui")
                        : (LanguageManager.Get("Non") ?? "Non"),
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
                MessageBox.Show(
                    LanguageManager.Get("Sélectionnez un serveur.") ?? "Sélectionnez un serveur.",
                    LanguageManager.Get("Info") ?? "Info",
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
                MessageBox.Show(
                    LanguageManager.Get("Sélectionnez un serveur.") ?? "Sélectionnez un serveur.",
                    LanguageManager.Get("Info") ?? "Info",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(
                string.Format(
                    LanguageManager.Get("Voulez-vous vraiment supprimer le serveur :\n\n{0} ?") ?? "Voulez-vous vraiment supprimer le serveur :\n\n{0} ?",
                    server.Name),
                LanguageManager.Get("Confirmation") ?? "Confirmation",
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
                MessageBox.Show(
                    LanguageManager.Get("Configuration enregistrée.") ?? "Configuration enregistrée.",
                    LanguageManager.Get("OK") ?? "OK",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                _ = RefreshAllStatesAsync();
            }
            else
            {
                MessageBox.Show(
                    LanguageManager.Get("Erreur lors de la sauvegarde.") ?? "Erreur lors de la sauvegarde.",
                    LanguageManager.Get("Erreur") ?? "Erreur",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------------------------------------------
        //  Test de CONNEXION aux serveurs
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
            bool serviceRunning = IsServiceRunning();
            bool trayRunning = IsTrayRunning();
            bool taskExists = TaskSchedulerHelper.ClientServiceTaskExists();
            bool trayTaskExists = ServiceInstaller.TrayTaskExists();

            // --- Bouton 1 : Automatique ON/OFF (tâche Service) ---
            _btnAutoService.Text = taskExists
                ? (LanguageManager.Get("Automatique ON")  ?? "Automatique ON")
                : (LanguageManager.Get("Automatique OFF") ?? "Automatique OFF");
            _btnAutoService.BackColor = taskExists
                ? Color.FromArgb(76, 175, 80)
                : Color.FromArgb(220, 60, 60);

            // --- Bouton 2 : ON/OFF (service + tray en cours) ---
            bool allRunning = serviceRunning && trayRunning;

            _btnServiceOnOff.Text = allRunning
                ? (LanguageManager.Get("ON")  ?? "ON")
                : (LanguageManager.Get("OFF") ?? "OFF");
            _btnServiceOnOff.BackColor = allRunning
                ? Color.FromArgb(76, 175, 80)
                : Color.FromArgb(220, 60, 60);

            // --- Icônes d'état ---
            _icoService.Image = serviceRunning ? _imgOk : _imgKo;
            _icoTray.Image    = trayRunning    ? _imgOk : _imgKo;

            // --- Texte explicatif à droite ---
            string status;
            if (taskExists && trayTaskExists)
                status = LanguageManager.Get("Auto (Service + Tray)") ?? "Auto (Service + Tray)";
            else if (taskExists)
                status = LanguageManager.Get("Auto Service uniquement") ?? "Auto Service uniquement";
            else
                status = LanguageManager.Get("Pas de démarrage auto") ?? "Pas de démarrage auto";

            _lblServiceStatus.Text = status;
            _lblServiceStatus.ForeColor = allRunning ? Theme.Ok : Theme.TextDim;
        }

        // --- Bouton 1 : Automatique (tâche Service) ---
        private void ToggleAutomaticTask()
        {
            try
            {
                if (TaskSchedulerHelper.ClientServiceTaskExists())
                {
                    var result = MessageBox.Show(
                        LanguageManager.Get("Voulez-vous vraiment désactiver le démarrage automatique ?\nLe service ne sera plus lancé au démarrage de Windows.") ?? "Voulez-vous vraiment désactiver le démarrage automatique ?\nLe service ne sera plus lancé au démarrage de Windows.",
                        LanguageManager.Get("Confirmation") ?? "Confirmation",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result != DialogResult.Yes)
                        return;

                    TaskSchedulerHelper.DeleteClientServiceTask();

                    MessageBox.Show(
                        LanguageManager.Get("Démarrage automatique désactivé.") ?? "Démarrage automatique désactivé.",
                        LanguageManager.Get("OK") ?? "OK",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    var result = MessageBox.Show(
                        LanguageManager.Get("Voulez-vous activer le démarrage automatique du service ?\nLe service sera lancé au démarrage de Windows.") ?? "Voulez-vous activer le démarrage automatique du service ?\nLe service sera lancé au démarrage de Windows.",
                        LanguageManager.Get("Confirmation") ?? "Confirmation",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result != DialogResult.Yes)
                        return;

                    TaskSchedulerHelper.CreateClientServiceTask();

                    MessageBox.Show(
                        LanguageManager.Get("Démarrage automatique activé.") ?? "Démarrage automatique activé.",
                        LanguageManager.Get("OK") ?? "OK",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                UpdateServiceStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show((LanguageManager.Get("Erreur") ?? "Erreur") + " : " + ex.Message);
            }
        }

        // --- Bouton 2 : ON/OFF manuel ---
        private void ToggleServiceManual()
        {
            if (IsServiceRunning())
            {
                StopService();
                StopTray();      // Arrête aussi le Tray
            }
            else
            {
                StartService();
                StartTray();     // Démarre aussi le Tray
            }
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
                        string.Format(
                            LanguageManager.Get("Impossible de trouver :\n{0}") ?? "Impossible de trouver :\n{0}",
                            servicePath),
                        LanguageManager.Get("Erreur") ?? "Erreur",
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
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(
                        LanguageManager.Get("Erreur au démarrage du service : {0}") ?? "Erreur au démarrage du service : {0}",
                        ex.Message));
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
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(
                        LanguageManager.Get("Erreur à l'arrêt du service : {0}") ?? "Erreur à l'arrêt du service : {0}",
                        ex.Message));
            }
        }

        // ---------------------------------------------
        //  TRAY : démarrage / arrêt manuel
        // ---------------------------------------------
        private bool IsTrayRunning()
        {
            return System.Diagnostics.Process.GetProcessesByName("MCEMonitorClient.Tray").Length > 0;
        }

        private void StartTray()
        {
            try
            {
                if (IsTrayRunning())
                    return;   // Déjà lancé

                string trayPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "MCEMonitor",
                    "MCEMonitorClient.Tray.exe");

                if (!System.IO.File.Exists(trayPath))
                {
                    // Fallback x86
                    trayPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "MCEMonitor",
                        "MCEMonitorClient.Tray.exe");
                }

                if (!System.IO.File.Exists(trayPath))
                {
                    MessageBox.Show(
                        string.Format(
                            LanguageManager.Get("Impossible de trouver :\n{0}") ?? "Impossible de trouver :\n{0}",
                            trayPath),
                        LanguageManager.Get("Erreur") ?? "Erreur",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = trayPath,
                    UseShellExecute = true
                });

                System.Threading.Thread.Sleep(800);

                UpdateServiceStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(
                        LanguageManager.Get("Erreur au démarrage du Tray : {0}") ?? "Erreur au démarrage du Tray : {0}",
                        ex.Message));
            }
        }

        private void StopTray()
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("MCEMonitorClient.Tray"))
                {
                    try { p.Kill(); } catch { }
                }

                System.Threading.Thread.Sleep(500);
                UpdateServiceStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(
                        LanguageManager.Get("Erreur à l'arrêt du Tray : {0}") ?? "Erreur à l'arrêt du Tray : {0}",
                        ex.Message));
            }
        }

        // ---------------------------------------------
        //  Chargement d'une icône d'état
        // ---------------------------------------------
        private static Image? LoadStateIcon(string fileName)
        {
            try
            {
                string exeDir = System.IO.Path.GetDirectoryName(Application.ExecutablePath) ?? "";
                string path = System.IO.Path.Combine(exeDir, "Resources", "Icons", fileName);
                if (!System.IO.File.Exists(path)) return null;

                // Charge depuis un flux pour éviter de verrouiller le fichier
                using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                return Image.FromStream(fs);
            }
            catch { return null; }
        }
    }
}