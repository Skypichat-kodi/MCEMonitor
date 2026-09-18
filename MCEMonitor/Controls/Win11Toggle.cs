using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MCEMonitor.Controls
{
    /// <summary>
    /// Switch ON/OFF style Windows 11 : capsule anti-aliasée, hover, état coché.
    /// </summary>
    public class Win11Toggle : Control
    {
        private bool _checked = false;
        private bool _isHover = false;

        public event EventHandler? CheckedChanged;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    Invalidate();
                    CheckedChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        // Couleurs personnalisables
        public Color OnColor { get; set; } = Color.FromArgb(76, 194, 255);       // bleu électrique
        public Color OnHoverColor { get; set; } = Color.FromArgb(93, 210, 255);
        public Color OffColor { get; set; } = Color.FromArgb(180, 180, 185);     // gris clair
        public Color OffHoverColor { get; set; } = Color.FromArgb(200, 200, 205);

        public Win11Toggle()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            Size = new Size(44, 22);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _isHover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Fond = couleur du parent
            if (Parent != null)
            {
                using var bgBrush = new SolidBrush(Parent.BackColor);
                g.FillRectangle(bgBrush, 0, 0, Width, Height);
            }

            int w = Width;
            int h = Height;
            int radius = (h - 1) / 2;

            // --- Couleur du fond de la capsule ---
            Color bgColor;

            if (Enabled)
            {
                if (_checked)
                    bgColor = _isHover ? OnHoverColor : OnColor;
                else
                    bgColor = _isHover ? OffHoverColor : OffColor;
            }
            else
            {
                bgColor = Color.FromArgb(220, 220, 220);
            }

            // --- Capsule (fond) ---
            var capsuleRect = new Rectangle(0, 0, w - 1, h - 1);

            using (var path = GetCapsulePath(capsuleRect, radius))
            using (var brush = new SolidBrush(bgColor))
            {
                g.FillPath(brush, path);
            }

            // --- Bordure fine ---
            Color borderColor = Enabled
                ? (_checked ? Color.FromArgb(50, 150, 210) : Color.FromArgb(150, 150, 155))
                : Color.FromArgb(200, 200, 200);

            using (var path = GetCapsulePath(capsuleRect, radius))
            using (var pen = new Pen(borderColor, 1))
            {
                g.DrawPath(pen, path);
            }

            // Marge autour du knob
            int margin = 3;
            int knobSize = h - (margin * 2);
            int knobX = _checked ? (w - knobSize - margin) : margin;
            int knobY = margin;

            using (var knobBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(knobBrush, knobX, knobY, knobSize, knobSize);
            }

            using (var knobPen = new Pen(Color.FromArgb(200, 200, 200), 1))
            {
                g.DrawEllipse(knobPen, knobX, knobY, knobSize, knobSize);
            }
        }

        private static GraphicsPath GetCapsulePath(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();

            path.AddArc(bounds.X, bounds.Y, d, d, 90, 180);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 180);
            path.CloseFigure();

            return path;
        }
    }
}