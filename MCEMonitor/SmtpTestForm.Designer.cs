namespace MCEMonitor
{
    partial class SmtpTestForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TextBox txtLog;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.txtLog = new System.Windows.Forms.TextBox();
            this.SuspendLayout();

            this.txtLog.Multiline = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.ReadOnly = true;
            this.txtLog.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtLog.Location = new System.Drawing.Point(10, 10);
            this.txtLog.Size = new System.Drawing.Size(560, 340);

            this.ClientSize = new System.Drawing.Size(580, 360);
            this.Controls.Add(this.txtLog);
            this.Text = LanguageManager.Get("Test SMTP - Détails") ?? "Test SMTP - Détails";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}

