using System;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace MCEMonitor
{
    public partial class SmtpTestForm : KryptonForm
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