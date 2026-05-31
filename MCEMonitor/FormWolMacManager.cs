using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MCEMonitor
{
    public partial class FormWolMacManager : Form
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
            // Format AA:BB:CC:DD:EE:FF
            return Regex.IsMatch(mac, @"^[0-9A-F]{2}(:[0-9A-F]{2}){5}$");
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            string mac = textBoxMac.Text.Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(mac))
            {
                MessageBox.Show("Veuillez entrer une adresse MAC.");
                return;
            }

            // Format sans ":" ? on les ajoute automatiquement
            if (!mac.Contains(":") && mac.Length == 12)
            {
                mac = string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));
            }

            if (!IsValidMac(mac))
            {
                MessageBox.Show("Format MAC invalide. Exemple : AA:BB:CC:DD:EE:FF");
                return;
            }

            if (listBoxMacs.Items.Contains(mac))
            {
                MessageBox.Show("Cette adresse MAC est déjà dans la liste.");
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

            // Remplace les tirets par des deux-points
            input = input.Replace("-", ":");

            // Supprime les espaces
            input = input.Replace(" ", "");

            // Si format collé (12 caractères hex), on ajoute les :
            if (!input.Contains(":") && input.Length == 12)
            {
                input = string.Join(":", Enumerable.Range(0, 6).Select(i => input.Substring(i * 2, 2)));
            }

            // Empêche le curseur de sauter en fin de texte
            int pos = textBoxMac.SelectionStart;
            textBoxMac.TextChanged -= textBoxMac_TextChanged;
            textBoxMac.Text = input;
            textBoxMac.SelectionStart = Math.Min(pos, textBoxMac.Text.Length);
            textBoxMac.TextChanged += textBoxMac_TextChanged;
        }          
    }
}

