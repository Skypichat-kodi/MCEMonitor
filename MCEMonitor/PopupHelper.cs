using System;
using System.Drawing;
using System.Windows.Forms;

namespace MCEMonitor
{
    public static class PopupHelper
    {
        public static void ShowBottomPopup(Form parent, string message, string title = "Information")
        {
            int popupWidth = 400;
            int popupHeight = 200;

            int x = parent.Left + (parent.Width - popupWidth) / 2;
            int y = parent.Top + parent.Height - popupHeight - 40;

            using (Form dummy = new Form())
            {
                dummy.StartPosition = FormStartPosition.Manual;
                dummy.FormBorderStyle = FormBorderStyle.None;
                dummy.ShowInTaskbar = false;
                dummy.Opacity = 0;
                dummy.Width = popupWidth;
                dummy.Height = popupHeight;
                dummy.Location = new Point(x, y);

                dummy.Load += (s, e) =>
                {
                    MessageBox.Show(dummy, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    dummy.Close();
                };

                dummy.ShowDialog(parent);
            }
        }
    }
}

