using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace MCEMonitor
{
    public partial class FormWolMacManager : KryptonForm
    {
        private string WhitelistPath;

        public FormWolMacManager()
        {
            InitializeComponent();
            InitPaths();
            LoadWhitelist();
        }

        private void InitPaths()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(programData, "MCEMonitor");

            Directory.CreateDirectory(dir);

            WhitelistPath = Path.Combine(dir, "AllowedWolMacs.txt");
        }

        private void LoadWhitelist()
        {
            listBoxMacs.Items.Clear();

            if (!File.Exists(WhitelistPath))
                return;

            foreach (var line in File.ReadAllLines(WhitelistPath))
            {
                string mac = line.Trim().ToUpper();
                if (mac.Length > 0)
                    listBoxMacs.Items.Add(mac);
            }
        }

        private void SaveWhitelist()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WhitelistPath));

            var cleaned = listBoxMacs.Items
                .Cast<string>()
                .Select(m => m.Trim().ToUpper())
                .Where(m => m.Length > 0)
                .Distinct()
                .OrderBy(m => m)
                .ToArray();

            File.WriteAllLines(WhitelistPath, cleaned);
        }

        private bool IsValidMac(string mac)
        {
            return Regex.IsMatch(mac, @"^[0-9A-F]{2}(:[0-9A-F]{2}){5}$");
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            string mac = textBoxMac.Text.Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(mac))
            {
                KryptonMessageBox.Show(LanguageManager.Get("Veuillez entrer une adresse MAC.") ?? "Veuillez entrer une adresse MAC.");
                return;
            }

            if (!mac.Contains(":") && mac.Length == 12)
            {
                mac = string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));
            }

            if (!IsValidMac(mac))
            {
                KryptonMessageBox.Show(LanguageManager.Get("Format MAC invalide. Exemple : AA:BB:CC:DD:EE:FF") ?? "Format MAC invalide. Exemple : AA:BB:CC:DD:EE:FF");
                return;
            }

            if (listBoxMacs.Items.Contains(mac))
            {
                KryptonMessageBox.Show(LanguageManager.Get("Cette adresse MAC est déjà dans la liste.") ?? "Cette adresse MAC est déjà dans la liste.");
                return;
            }

            listBoxMacs.Items.Add(mac);
            textBoxMac.Clear();
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            if (listBoxMacs.SelectedItem != null)
                listBoxMacs.Items.Remove(listBoxMacs.SelectedItem);
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            SaveWhitelist();
            this.Close();
        }

        private void textBoxMac_TextChanged(object sender, EventArgs e)
        {
            string input = textBoxMac.Text.Trim().ToUpper();

            input = input.Replace("-", ":");
            input = input.Replace(" ", "");

            if (!input.Contains(":") && input.Length == 12)
            {
                input = string.Join(":", Enumerable.Range(0, 6).Select(i => input.Substring(i * 2, 2)));
            }

            int pos = textBoxMac.SelectionStart;
            textBoxMac.TextChanged -= textBoxMac_TextChanged;
            textBoxMac.Text = input;
            textBoxMac.SelectionStart = Math.Min(pos, textBoxMac.Text.Length);
            textBoxMac.TextChanged += textBoxMac_TextChanged;
        }
    }
}