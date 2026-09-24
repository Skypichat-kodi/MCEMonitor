using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace MCEMonitor
{
    /// <summary>
    /// Dialogue de confirmation custom, entièrement thémé Krypton.
    /// Utilise une icône PNG depuis Resources/Icons/.
    /// </summary>
    public class ConfirmDialog : KryptonForm
    {
        private KryptonPictureBox _picIcon;
        private KryptonLabel _lblMessage;
        private KryptonButton _btnYes;
        private KryptonButton _btnNo;

        public bool Result { get; private set; } = false;

        public ConfirmDialog(string message, string title, string yesText, string noText, bool warning = true)
        {
            InitializeUI(title, yesText, noText, warning);
            _lblMessage.Text = message;
        }

        private void InitializeUI(string title, string yesText, string noText, bool warning)
        {
            this.Text = title;
            this.ClientSize = new Size(440, 200);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ShowInTaskbar = false;

            // ? Icône PNG au lieu du caractère Unicode "?"
            _picIcon = new KryptonPictureBox
            {
                Location = new Point(20, 20),
                Size = new Size(48, 48),
                SizeMode = PictureBoxSizeMode.StretchImage
            };

            // Charge l'image depuis Resources/Icons/
            string iconName = warning ? "warning.png" : "info.png";
            string iconPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Resources",
                "Icons",
                iconName
            );

            if (File.Exists(iconPath))
            {
                _picIcon.Image = Image.FromFile(iconPath);
            }
            else
            {
                // Fallback : icône système si le PNG est absent
                _picIcon.Image = warning
                    ? SystemIcons.Warning.ToBitmap()
                    : SystemIcons.Information.ToBitmap();
            }

            _lblMessage = new KryptonLabel
            {
                AutoSize = false,
                Location = new Point(85, 20),
                Size = new Size(335, 100),
                Font = new Font("Segoe UI", 10F)
            };

            _btnYes = new KryptonButton
            {
                Text = yesText,
                Location = new Point(180, 140),
                Size = new Size(120, 35)
            };
            _btnYes.Click += (s, e) => { Result = true; this.DialogResult = DialogResult.Yes; this.Close(); };

            _btnNo = new KryptonButton
            {
                Text = noText,
                Location = new Point(310, 140),
                Size = new Size(120, 35)
            };
            _btnNo.Click += (s, e) => { Result = false; this.DialogResult = DialogResult.No; this.Close(); };

            this.Controls.Add(_picIcon);
            this.Controls.Add(_lblMessage);
            this.Controls.Add(_btnYes);
            this.Controls.Add(_btnNo);

            this.AcceptButton = _btnYes;
            this.CancelButton = _btnNo;
        }

        public static bool Show(IWin32Window owner, string message, string title,
                                string yesText = "Oui", string noText = "Non",
                                bool warning = true)
        {
            using var dlg = new ConfirmDialog(message, title, yesText, noText, warning);
            dlg.ShowDialog(owner);
            return dlg.Result;
        }
    }
}