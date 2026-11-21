using Krypton.Toolkit;

namespace MCEMonitor
{
    partial class MediaMonitorLiveForm
    {
        private System.ComponentModel.IContainer components = null;

        private KryptonLabel lblCpu;
        private KryptonLabel lblGpu;
        private KryptonLabel lblTemp;
        private KryptonLabel lblNetwork;
        private KryptonLabel lblDisk;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.lblCpu = new KryptonLabel();
            this.lblGpu = new KryptonLabel();
            this.lblTemp = new KryptonLabel();
            this.lblNetwork = new KryptonLabel();
            this.lblDisk = new KryptonLabel();

            this.SuspendLayout();

            lblCpu.Location = new System.Drawing.Point(20, 20);
            lblGpu.Location = new System.Drawing.Point(20, 50);
            lblTemp.Location = new System.Drawing.Point(20, 80);
            lblNetwork.Location = new System.Drawing.Point(20, 110);
            lblDisk.Location = new System.Drawing.Point(20, 140);

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

            this.Text = LanguageManager.Get("MediaMonitor - Temps réel") ?? "MediaMonitor - Temps réel";
            this.ClientSize = new System.Drawing.Size(300, 200);

            this.ResumeLayout(false);
        }
    }
}