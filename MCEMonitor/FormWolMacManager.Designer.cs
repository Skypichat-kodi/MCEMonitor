using Krypton.Toolkit;

namespace MCEMonitor
{
    partial class FormWolMacManager
    {
        private System.ComponentModel.IContainer components = null;

        private KryptonListBox listBoxMacs;
        private KryptonTextBox textBoxMac;
        private KryptonButton btnAdd;
        private KryptonButton btnRemove;
        private KryptonButton btnClose;
        private KryptonLabel labelTitle;
        private KryptonLabel labelAdd;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.listBoxMacs = new KryptonListBox();
            this.textBoxMac = new KryptonTextBox();
            this.btnAdd = new KryptonButton();
            this.btnRemove = new KryptonButton();
            this.btnClose = new KryptonButton();
            this.labelTitle = new KryptonLabel();
            this.labelAdd = new KryptonLabel();
            this.SuspendLayout();

            // labelTitle
            this.labelTitle.Text = LanguageManager.Get("Adresses MAC autorisées (Whitelist WOL)") ?? "Adresses MAC autorisées (Whitelist WOL)";
            this.labelTitle.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.labelTitle.Location = new System.Drawing.Point(12, 9);
            this.labelTitle.Size = new System.Drawing.Size(350, 23);
            this.labelTitle.AutoSize = false;

            // listBoxMacs
            this.listBoxMacs.FormattingEnabled = true;
            this.listBoxMacs.Location = new System.Drawing.Point(15, 35);
            this.listBoxMacs.Size = new System.Drawing.Size(330, 154);

            // labelAdd
            this.labelAdd.Text = LanguageManager.Get("Ajouter une adresse MAC :") ?? "Ajouter une adresse MAC :";
            this.labelAdd.Location = new System.Drawing.Point(12, 200);
            this.labelAdd.Size = new System.Drawing.Size(200, 20);
            this.labelAdd.AutoSize = false;

            // textBoxMac
            this.textBoxMac.Location = new System.Drawing.Point(15, 225);
            this.textBoxMac.Size = new System.Drawing.Size(250, 23);

            // btnAdd
            this.btnAdd.Text = LanguageManager.Get("+") ?? "+";
            this.btnAdd.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.btnAdd.Location = new System.Drawing.Point(275, 220);
            this.btnAdd.Size = new System.Drawing.Size(40, 32);
            this.btnAdd.Click += new System.EventHandler(this.btnAdd_Click);

            // btnRemove
            this.btnRemove.Text = LanguageManager.Get("Supprimer") ?? "Supprimer";
            this.btnRemove.Location = new System.Drawing.Point(15, 270);
            this.btnRemove.Size = new System.Drawing.Size(120, 30);
            this.btnRemove.Click += new System.EventHandler(this.btnRemove_Click);

            // btnClose
            this.btnClose.Text = LanguageManager.Get("Fermer") ?? "Fermer";
            this.btnClose.Location = new System.Drawing.Point(225, 270);
            this.btnClose.Size = new System.Drawing.Size(120, 30);
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);

            // FormWolMacManager
            this.textBoxMac.TextChanged += new System.EventHandler(this.textBoxMac_TextChanged);
            this.ClientSize = new System.Drawing.Size(360, 320);
            this.Controls.Add(this.labelTitle);
            this.Controls.Add(this.listBoxMacs);
            this.Controls.Add(this.labelAdd);
            this.Controls.Add(this.textBoxMac);
            this.Controls.Add(this.btnAdd);
            this.Controls.Add(this.btnRemove);
            this.Controls.Add(this.btnClose);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Whitelist Wake-on-LAN";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}