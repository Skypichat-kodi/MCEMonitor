using System;
using System.Windows.Forms;
using MCEMonitor.Services;

namespace MCEMonitor
{
    public partial class MediaMonitorLiveForm : Form
    {
        private readonly MediaMonitorService _media;
        private readonly System.Windows.Forms.Timer _timer;

        public MediaMonitorLiveForm(MediaMonitorService media)
        {
            _media = media;

            InitializeComponent();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += UpdateStatus;
            _timer.Start();
        }

        private void UpdateStatus(object sender, EventArgs e)
        {
            var s = _media.GetLiveStatus();

            lblCpu.Text = $"CPU : {s.CpuUsage}%";
            lblGpu.Text = $"GPU : {s.GpuUsage}%";
            lblTemp.Text = $"Temp CPU : {s.CpuTemp}°C";
            lblNetwork.Text = $"Réseau : {s.NetworkUsage} Mb/s";
            lblDisk.Text = $"Disque : {s.DiskUsage}%";
        }
    }
}


