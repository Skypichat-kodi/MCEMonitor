using System;
using System.IO;
using System.Linq;
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

            File.WriteAllLines(
                WhitelistPath,
                listBoxMacs.Items.Cast<string>()
            );
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

            // Vérification simple
            if (mac.Length != 17 || mac.Count(c => c == ':') != 5)
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
    }
}

