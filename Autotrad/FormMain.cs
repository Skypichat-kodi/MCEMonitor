using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace Autotrad
{
    public partial class FormMain : Form
    {
        private string _lastOpenedFile = "";
        private Dictionary<string, string> _existingKeys = new();

        public FormMain()
        {
            InitializeComponent();
        }

        private void ouvrirToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog();
            dlg.Filter = "Fichiers C# (*.cs)|*.cs";

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _lastOpenedFile = dlg.FileName;

                LoadExistingJsonKeys();

                var list = Scanner.ScanFile(dlg.FileName, _existingKeys);
                dataGridView1.DataSource = list;

                SetupColumns();
                FillPreviewColumn();
            }
        }

        private void LoadExistingJsonKeys()
        {
            string langDir = Path.Combine(AppContext.BaseDirectory, "languages");
            string frPath = Path.Combine(langDir, "fr-FR.json");

            if (File.Exists(frPath))
            {
                var json = File.ReadAllText(frPath);
                _existingKeys = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            }
            else
            {
                _existingKeys = new Dictionary<string, string>();
            }
        }

        private void SetupColumns()
        {
            dataGridView1.Columns.Clear();

            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "LineNumber",
                HeaderText = "Ligne",
                Width = 60
            });

            var colPreview = new DataGridViewTextBoxColumn
            {
                HeaderText = "Aperçu",
                Name = "Preview",
                Width = 450
            };
            dataGridView1.Columns.Add(colPreview);

            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Text",
                HeaderText = "Texte détecté",
                Width = 200
            });

            dataGridView1.Columns.Add(new DataGridViewCheckBoxColumn
            {
                DataPropertyName = "Selected",
                HeaderText = "Traduire ?",
                Width = 80
            });
        }

        private void FillPreviewColumn()
        {
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row.DataBoundItem is ScanResult item)
                {
                    row.Cells["Preview"].Value = item.Preview;
                }
            }
        }

        private void dataGridView1_RowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            var row = dataGridView1.Rows[e.RowIndex];
            if (row.DataBoundItem is ScanResult item)
            {
                if (item.IsMismatch)
                {
                    row.DefaultCellStyle.BackColor = System.Drawing.Color.LightPink;
                    return;
                }

                if (item.IsTranslated)
                {
                    row.DefaultCellStyle.BackColor = System.Drawing.Color.LightGreen;
                }
            }
        }

        private void dataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            if (dataGridView1.Rows[e.RowIndex].DataBoundItem is not ScanResult item)
                return;

            if (string.IsNullOrEmpty(_lastOpenedFile))
                return;

            int line = item.LineNumber;

            if (TryOpen("code", $"\"{_lastOpenedFile}\" -g {line}"))
                return;

            if (TryOpen("notepad++", $"\"{_lastOpenedFile}\" -n{line}"))
                return;

            TryOpen("notepad", $"\"{_lastOpenedFile}\"");
        }

        private bool TryOpen(string exe, string args)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void btnExport_Click(object sender, EventArgs e)
        {
            if (dataGridView1.DataSource is not List<ScanResult> list)
                return;

            string module = Utils.GetModuleFromFilename(_lastOpenedFile);

            string langDir = Path.Combine(AppContext.BaseDirectory, "languages");
            Directory.CreateDirectory(langDir);

            string frPath = Path.Combine(langDir, "fr-FR.json");
            string enPath = Path.Combine(langDir, "en-GB.json");

            foreach (var item in list.Where(x => x.Selected))
            {
                if (!item.IsTranslated)
                {
                    string key = Utils.GenerateKeyFromText(module, item.Text);

                    string left = Utils.ExtractLeftPart(item.FullLine);
                    string newLine =
                        $"{left}LanguageManager.Get(\"{key}\") ?? \"{item.Text}\";";

                    Utils.ReplaceLineInFile(_lastOpenedFile, item.LineNumber, newLine);

                    Utils.AddToJson(frPath, key, item.Text);
                    Utils.AddToJson(enPath, key, "");
                }
                else
                {
                    string key = Utils.ExtractKeyFromLine(item.FullLine);
                    string fallback = Utils.ExtractFallbackText(item.FullLine);

                    Utils.AddToJson(frPath, key, fallback);
                    Utils.AddToJson(enPath, key, "");
                }
            }

            MessageBox.Show("Traductions appliquées.");
        }
    }
}

