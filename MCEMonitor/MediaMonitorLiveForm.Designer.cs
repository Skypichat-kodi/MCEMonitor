namespace MCEMonitor
{
    partial class MediaMonitorLiveForm
    {
        private System.ComponentModel.IContainer components = null;

        private Label lblCpu;
        private Label lblGpu;
        private Label lblTemp;
        private Label lblNetwork;
        private Label lblDisk;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.lblCpu = new Label();
            this.lblGpu = new Label();
            this.lblTemp = new Label();
            this.lblNetwork = new Label();
            this.lblDisk = new Label();

            this.SuspendLayout();

            lblCpu.Location = new Point(20, 20);
            lblGpu.Location = new Point(20, 50);
            lblTemp.Location = new Point(20, 80);
            lblNetwork.Location = new Point(20, 110);
            lblDisk.Location = new Point(20, 140);

            lblCpu.AutoSize = true;
            lblGpu.AutoSize = true;
            lblTemp.AutoSize = true;
            lblNetwork.AutoSize = true;
            lblDisk.AutoSize = true;

            this.Controls.Add(lblCpu);
            this.Controls.Add(lblGpu);
            this.Controls.Add(lblTemp);
            this.Controls.Add(lblNetwork);
            this.Controls.Add(lblDisk);

            this.Text = LanguageManager.Get("MediaMonitorForm.Title") ?? "MediaMonitor - Temps réel";
            this.ClientSize = new Size(300, 200);

            this.ResumeLayout(false);
        }
    }
}

