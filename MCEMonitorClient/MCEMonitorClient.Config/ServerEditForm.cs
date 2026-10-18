using System;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using MCEMonitorClient.Config.Models;

namespace MCEMonitorClient.Config
{
    public class ServerEditForm : Form
    {
        private readonly ServerEntry _entry;
        private readonly bool _isNew;

        // Contrôles
        private TextBox _txtName = null!;
        private ComboBox _cmbType = null!;
        private TextBox _txtUrl = null!;
        private NumericUpDown _numPort = null!;
        private TextBox _txtUsername = null!;
        private TextBox _txtPassword = null!;
        private Button _btnTest = null!;
        private Button _btnSave = null!;
        private Button _btnCancel = null!;
        private Label _lblTestResult = null!;

        public ServerEntry Result => _entry;

        public ServerEditForm(ServerEntry? existing = null)
        {
            _isNew = (existing == null);
            _entry = existing ?? new ServerEntry();

            InitializeUI();

            if (!_isNew)
                LoadEntry();
        }

        // ---------------------------------------------
        //  Construction de l'UI
        // ---------------------------------------------
        private void InitializeUI()
        {
            Text = _isNew ? "Ajouter un serveur" : "Modifier le serveur";
            Size = new Size(520, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font(Theme.FontFamily, 9F);

            int labelX = 20;
            int fieldX = 150;
            int fieldW = 320;
            int y = 20;
            int step = 32;

            // --- Nom ---
            AddLabel("Nom :", labelX, y);
            _txtName = AddTextBox(fieldX, y, fieldW);
            y += step;

            // --- Type ---
            AddLabel("Type :", labelX, y);
            _cmbType = new ComboBox
            {
                Location = new Point(fieldX, y),
                Width = fieldW,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat
            };
            _cmbType.Items.AddRange(new object[]
            {
                "SystemMonitor",
                "RomMonitor",
                "MediaMonitor",
                "NetMonitor", 
                "Inconnu" 
            });
            _cmbType.SelectedIndex = 0;
            
            Controls.Add(_cmbType);
            y += step;

            // --- URL + Port ---
            AddLabel("URL :", labelX, y);

            _txtUrl = new TextBox
            {
                Location = new Point(fieldX, y),
                Width = fieldW - 100,       // ? réduit pour le port
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtUrl.Text = "http://";
            Controls.Add(_txtUrl);

            var lblPort = new Label
            {
                Text = "Port :",
                Location = new Point(fieldX + fieldW - 90, y + 4),
                Width = 40,
                ForeColor = Theme.TextDim
            };
            Controls.Add(lblPort);

            _numPort = new NumericUpDown
            {
                Location = new Point(fieldX + fieldW - 50, y),
                Width = 50,
                Minimum = 1,
                Maximum = 65535,
                Value = 8083,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_numPort);

            y += step;

            // --- Login ---
            AddLabel("Login :", labelX, y);
            _txtUsername = AddTextBox(fieldX, y, fieldW);
            y += step;

            // --- Mot de passe + bouton œil ---
            AddLabel("Mot de passe :", labelX, y);

            _txtPassword = new TextBox
            {
                Location = new Point(fieldX, y),
                Width = fieldW - 40,          // ? réduit pour laisser la place au bouton
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                UseSystemPasswordChar = true
            };
            Controls.Add(_txtPassword);

            // Charge les deux icônes
            var imgEye    = LoadIcon("eye.png");
            var imgEyeOff = LoadIcon("eye-off.png");

            // PictureBox cliquable : œil ouvert = cliquer pour voir,
            //                       œil barré = cliquer pour masquer
            var btnEye = new PictureBox
            {
                Image    = imgEye,              // au départ le mot de passe est masqué ? on montre "eye" (voir)
                Location = new Point(fieldX + fieldW - 26, y),
                Size     = new Size(22, 22),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            btnEye.Click += (s, e) =>
            {
                _txtPassword.UseSystemPasswordChar = !_txtPassword.UseSystemPasswordChar;
                btnEye.Image = _txtPassword.UseSystemPasswordChar ? imgEye : imgEyeOff;
            };
            Controls.Add(btnEye);

            y += step;

            // --- Bouton Tester ---
            _btnTest = new Button
            {
                Text = "Tester la connexion",
                Location = new Point(fieldX, y),
                Width = 160,
                Height = 28,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnTest.FlatAppearance.BorderColor = Theme.Border;
            _btnTest.Click += async (s, e) => await TestConnectionAsync();
            Controls.Add(_btnTest);
            y += 40;

            // --- Résultat du test ---
            _lblTestResult = new Label
            {
                Location = new Point(fieldX, y),
                Width = fieldW,
                Height = 20,
                ForeColor = Theme.TextDim,
                Text = ""
            };
            Controls.Add(_lblTestResult);

            // --- Boutons Annuler / Enregistrer ---
            int buttonY = ClientSize.Height - 50;

            _btnCancel = new Button
            {
                Text = "Annuler",
                Location = new Point(ClientSize.Width - 260, buttonY),
                Width = 110,
                Height = 32,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.Cancel
            };
            _btnCancel.FlatAppearance.BorderColor = Theme.Border;
            Controls.Add(_btnCancel);

            _btnSave = new Button
            {
                Text = "Enregistrer",
                Location = new Point(ClientSize.Width - 140, buttonY),
                Width = 110,
                Height = 32,
                BackColor = Theme.Accent,
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Theme.FontFamily, 9F, FontStyle.Bold)
            };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += (s, e) => SaveAndClose();
            Controls.Add(_btnSave);

            AcceptButton = _btnSave;
            CancelButton = _btnCancel;
        }

        // ---------------------------------------------
        //  Helpers UI
        // ---------------------------------------------
        private void AddLabel(string text, int x, int y)
        {
            var lbl = new Label
            {
                Text = text,
                Location = new Point(x, y + 4),
                Width = 125,
                ForeColor = Theme.TextDim
            };
            Controls.Add(lbl);
        }

        private TextBox AddTextBox(int x, int y, int width)
        {
            var txt = new TextBox
            {
                Location = new Point(x, y),
                Width = width,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(txt);
            return txt;
        }

        // ---------------------------------------------
        //  Chargement / sauvegarde
        // ---------------------------------------------
        private void LoadEntry()
        {
            _txtName.Text = _entry.Name;
            _txtUrl.Text = _entry.BaseUrl;
            _numPort.Value = Math.Clamp(_entry.Port, (int)_numPort.Minimum, (int)_numPort.Maximum);
            _txtUsername.Text = _entry.Username;
            _txtPassword.Text = _entry.Password;

            int idx = _cmbType.Items.IndexOf(_entry.ServiceType);
            _cmbType.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private void SaveAndClose()
        {
            // Validation
            if (string.IsNullOrWhiteSpace(_txtName.Text))
            {
                MessageBox.Show("Le nom est obligatoire.", "Erreur",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_txtUrl.Text))
            {
                MessageBox.Show("L'URL est obligatoire.", "Erreur",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // On ne rejette plus les URLs sans port explicite :
            // FullUrl sera reconstruite avec le champ Port.
            _entry.Name = _txtName.Text.Trim();
            _entry.BaseUrl = _txtUrl.Text.Trim().TrimEnd('/');
            _entry.Port = (int)_numPort.Value;
            _entry.Username = _txtUsername.Text.Trim();
            _entry.Password = _txtPassword.Text;
            _entry.ServiceType = _cmbType.SelectedItem?.ToString() ?? "Inconnu";

            DialogResult = DialogResult.OK;
            Close();
        }

        // ---------------------------------------------
        //  Test de connexion HTTP + Détection du service
        // ---------------------------------------------
        private async Task TestConnectionAsync()
        {
            _btnTest.Enabled = false;
            _lblTestResult.ForeColor = Theme.TextDim;
            _lblTestResult.Text = "Test en cours...";

            try
            {
                string baseUrl = _txtUrl.Text.Trim().TrimEnd('/');
                int port = (int)_numPort.Value;
                string url = $"{baseUrl}:{port}/api/summary";

                var handler = new HttpClientHandler
                {
                    Credentials = new NetworkCredential(
                        _txtUsername.Text.Trim(),
                        _txtPassword.Text),
                    PreAuthenticate = true
                };

                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                using var resp = await http.GetAsync(url);

                if (!resp.IsSuccessStatusCode)
                {
                    _lblTestResult.ForeColor = Theme.Critical;
                    _lblTestResult.Text = $"[KO] Erreur HTTP {(int)resp.StatusCode}";
                    return;
                }

                // Lecture du JSON pour extraire le champ "service"
                string json = await resp.Content.ReadAsStringAsync();
                string detectedService = "Inconnu";

                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("service", out var serviceProp))
                        detectedService = serviceProp.GetString() ?? "Inconnu";
                }
                catch { /* JSON invalide, on garde "Inconnu" */ }

                // --- Mise à jour de l'UI ---
                _lblTestResult.ForeColor = Theme.Ok;
                _lblTestResult.Text = $"[OK] Service détecté : {detectedService}";

                // 1) Mise à jour du ComboBox (si le service est dans la liste)
                int idx = _cmbType.Items.IndexOf(detectedService);
                if (idx >= 0)
                    _cmbType.SelectedIndex = idx;

                // 2) Mise à jour du nom si vide (ou si c'est encore le nom par défaut)
                if (string.IsNullOrWhiteSpace(_txtName.Text) ||
                    _txtName.Text == "http://")
                {
                    _txtName.Text = $"{detectedService} ({_txtUrl.Text.Trim()}:{port})";
                }

                // 3) Popup de confirmation
                var result = MessageBox.Show(
                    $"Connexion réussie !\n\n" +
                    $"Service détecté : {detectedService}\n" +
                    $"Serveur : {baseUrl}:{port}\n\n" +
                    $"Voulez-vous utiliser ce nom ?\n" +
                    $"? {detectedService} ({_txtUrl.Text.Trim()}:{port})",
                    "Service détecté",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                {
                    _txtName.Text = $"{detectedService} ({_txtUrl.Text.Trim()}:{port})";
                }
            }
            catch (TaskCanceledException)
            {
                _lblTestResult.ForeColor = Theme.Warning;
                _lblTestResult.Text = "[KO] Timeout (5s)";
            }
            catch (Exception ex)
            {
                _lblTestResult.ForeColor = Theme.Critical;
                _lblTestResult.Text = "[KO] " + ex.Message;
            }
            finally
            {
                _btnTest.Enabled = true;
            }
        }
        
        private static Image? LoadIcon(string fileName)
        {
            try
            {
                string exeDir = System.IO.Path.GetDirectoryName(Application.ExecutablePath) ?? "";
                string path = System.IO.Path.Combine(exeDir, "Resources", "Icons", fileName);
                if (!System.IO.File.Exists(path)) return null;

                using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                return Image.FromStream(fs);
            }
            catch { return null; }
        }        
    }
}