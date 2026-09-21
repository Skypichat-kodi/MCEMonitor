using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MCEMonitor.Controls
{
    public class RoundedPanel : Panel
    {
        private int _cornerRadius = 8;
        private Color _borderColor = Color.FromArgb(210, 210, 210);
        private int _borderWidth = 1;

        public int CornerRadius
        {
            get => _cornerRadius;
            set
            {
                _cornerRadius = Math.Max(0, value);
                UpdateRegion();
                Invalidate();
            }
        }

        public Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; Invalidate(); }
        }

        public int BorderWidth
        {
            get => _borderWidth;
            set
            {
                _borderWidth = Math.Max(0, value);
                Invalidate();
            }
        }

        public RoundedPanel()
        {
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);

            BackColor = Color.FromArgb(240, 240, 240);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRegion();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0)
                return;

            // Le path utilisé pour la Region couvre TOUT le contrôle
            using (var path = CreateRoundedPath(ClientRectangle, _cornerRadius))
                Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Fond : utilise un path légèrement rentré pour ne pas se faire couper
            var fillRect = ClientRectangle;
            fillRect.Inflate(-1, -1);

            using (var path = CreateRoundedPath(fillRect, _cornerRadius))
            using (var brush = new SolidBrush(BackColor))
            {
                e.Graphics.FillPath(brush, path);
            }

            // Bordure : path encore rentré, pour ne pas être coupé par la Region
            if (_borderWidth > 0)
            {
                var borderRect = ClientRectangle;
                borderRect.Width -= 1;
                borderRect.Height -= 1;
                borderRect.Inflate(-_borderWidth / 2, -_borderWidth / 2);

                // Ajuste le rayon pour que la courbure reste correcte
                int adjustedRadius = Math.Max(1, _cornerRadius - (_borderWidth / 2));

                using (var path = CreateRoundedPath(borderRect, adjustedRadius))
                using (var pen = new Pen(_borderColor, _borderWidth))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();

            if (rect.Width <= 0 || rect.Height <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            int d = radius * 2;

            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}