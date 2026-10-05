using System;
using System.Text;
using System.Windows.Forms;

namespace MCEMonitorClient.Config
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // ? IMPORTANT : enregistre le provider pour Encoding.GetEncoding(850)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new MainForm());
        }
    }
}