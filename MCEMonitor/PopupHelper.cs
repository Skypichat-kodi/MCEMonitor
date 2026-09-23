using System;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace MCEMonitor
{
    public static class PopupHelper
    {
        public static void ShowBottomPopup(Form parent, string message, string title = "Information")
        {
            KryptonMessageBox.Show(
                message,
                title,
                KryptonMessageBoxButtons.OK,
                KryptonMessageBoxIcon.Information
            );
        }
    }
}