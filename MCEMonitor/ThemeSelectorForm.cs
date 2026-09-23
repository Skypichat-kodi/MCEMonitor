using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace MCEMonitor
{
    /// <summary>
    /// Formulaire de sélection du thème Krypton.
    /// Utilise KryptonThemeListBox (inclus dans Krypton.Toolkit).
    /// Le thème est appliqué en direct et sauvegardé dans theme.config.
    /// </summary>
    public class ThemeSelectorForm : KryptonForm
    {
        private KryptonThemeListBox _themeList;
        private KryptonButton _btnClose;
        private KryptonLabel _lblInfo;

        // Chemin du fichier de config
        private static string ThemeConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MCEMonitor",
            "theme.config"
        );

        public ThemeSelectorForm()
        {
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Text = LanguageManager.Get("Choix du thème") ?? "Choix du thème";
            this.ClientSize = new Size(440, 380);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;

            _lblInfo = new KryptonLabel
            {
                Text = LanguageManager.Get("Sélectionnez un thème (appliqué immédiatement) :")
                    ?? "Sélectionnez un thème (appliqué immédiatement) :",
                Location = new Point(20, 15),
                Size = new Size(400, 25),
                Font = new Font("Segoe UI", 9F)
            };

            _themeList = new KryptonThemeListBox
            {
                Location = new Point(20, 45),
                Size = new Size(400, 280)
            };

            // Restaure la sélection actuelle (sans réappliquer le thème)
            var current = LoadSavedTheme();
            if (_themeList.Items.Contains(current))
                _themeList.SelectedItem = current;

            // Applique le thème dès qu'on change la sélection
            _themeList.SelectedIndexChanged += ThemeList_SelectedIndexChanged;

            _btnClose = new KryptonButton
            {
                Text = LanguageManager.Get("Fermer") ?? "Fermer",
                Location = new Point(300, 335),
                Size = new Size(120, 32)
            };
            _btnClose.Click += (s, e) => this.Close();

            this.Controls.Add(_lblInfo);
            this.Controls.Add(_themeList);
            this.Controls.Add(_btnClose);
        }

        private void ThemeList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_themeList.SelectedItem is PaletteMode mode)
            {
                // ? Utilise l'instance partagée exposée par Program
                Program.SharedManager.GlobalPaletteMode = mode;

                SaveTheme(mode);
            }
        }

        // ============================================================
        //  Persistance
        // ============================================================

        private static void SaveTheme(PaletteMode mode)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ThemeConfigPath)!);
                File.WriteAllText(ThemeConfigPath, mode.ToString());
            }
            catch
            {
                // silencieux
            }
        }

        public static PaletteMode LoadSavedTheme()
        {
            try
            {
                if (File.Exists(ThemeConfigPath))
                {
                    string content = File.ReadAllText(ThemeConfigPath).Trim();
                    if (Enum.TryParse<PaletteMode>(content, out var mode))
                        return mode;
                }
            }
            catch { }

            // Thème par défaut
            return PaletteMode.SparkleBlueDarkMode;
        }
    }
}