using System;
using System.Windows.Forms;

namespace MCEMonitor
{
    public partial class SmtpTestForm : Form
    {
        public SmtpTestForm()
        {
            InitializeComponent();
        }

        public void Log(string message)
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        }
    }
}

