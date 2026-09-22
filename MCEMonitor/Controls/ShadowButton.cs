using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MCEMonitor.Controls
{
    public class ShadowButton : Button
    {
        private int _shadowOffset = 2;
        private int _cornerRadius = 4;
        private Color _shadowColor = Color.FromArgb(80, 0, 0, 0);

        private bool _hovered = false;
        private bool _pressed = false;

        /// <summary>
        /// Couleur de surface sur laquelle le bouton est posé.
        /// Si Empty, on tente une auto-détection ; sinon on utilise la valeur fournie.
        /// </summary>
        public Color SurfaceColor { get; set; } = Color.Empty;

        public int ShadowOffset
        {
            get => _shadowOffset;
            set { _shadowOffset = Math.Max(0, value); Invalidate(); }
        }

        public int CornerRadius
        {
            get => _cornerRadius;
            set { _cornerRadius = Math.Max(0, value); Invalidate(); }
        }

        public Color ShadowColor
        {
            get => _shadowColor;
            set { _shadowColor = value; Invalidate(); }
        }

        public ShadowButton()
        {
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
        }

        // ----- États -----

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true;  Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; _pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = true;  Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e)   { base.OnMouseUp(e);   _pressed = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // ----- Rendu du fond -----

        private Color GetSurfaceColor()
        {
            if (SurfaceColor != Color.Empty)
                return SurfaceColor;

            // Remonte la hiérarchie jusqu'à trouver un TabPage (fiable)
            // sinon retombe sur la couleur du parent non-transparent
            Control? current = Parent;
            Color last = SystemColors.Control;

            while (current != null)
            {
                if (current is TabPage || current is Form)
                    return current.BackColor != Color.Transparent
                        ? current.BackColor
                        : Color.White;

                if (current.BackColor != Color.Transparent)
                    last = current.BackColor;

                current = current.Parent;
            }

            return last;
        }

        // ----- Couleur de remplissage -----

        private Color ComputeFillColor()
        {
            if (!Enabled)
                return SystemColors.Control;

            if (_pressed && FlatAppearance.MouseDownBackColor != Color.Empty)
                return FlatAppearance.MouseDownBackColor;

            if (_hovered && FlatAppearance.MouseOverBackColor != Color.Empty)
                return FlatAppearance.MouseOverBackColor;

            Color baseColor = BackColor;

            if (_pressed)
                return DarkenColor(baseColor, 0.18f);

            if (_hovered)
                return LightenColor(baseColor, 0.22f);   // ? survol plus visible

            return baseColor;
        }

        // ----- Rendu principal -----

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // ? AJOUT : efface tout résidu avant de dessiner
            g.Clear(GetSurfaceColor());

            var btnRect = new Rectangle(
                0, 0,
                Width - _shadowOffset - 1,
                Height - _shadowOffset - 1);

            // 1) Ombre
            using (var shadowPath = CreateRoundedPath(
                new Rectangle(_shadowOffset, _shadowOffset, btnRect.Width, btnRect.Height),
                _cornerRadius))
            using (var shadowBrush = new SolidBrush(_shadowColor))
            {
                g.FillPath(shadowBrush, shadowPath);
            }

            // 2) Fond
            Color fill = ComputeFillColor();

            using (var path = CreateRoundedPath(btnRect, _cornerRadius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }

            // 3) Contour léger (rend le survol encore plus visible)
            if (Enabled)
            {
                Color borderColor = _hovered
                    ? Color.FromArgb(120, 76, 194, 255)   // bleu Win11 au survol
                    : DarkenColor(fill, 0.15f);

                using var path = CreateRoundedPath(btnRect, _cornerRadius);
                using var pen = new Pen(borderColor, 1);
                g.DrawPath(pen, path);
            }

            // 4) Image
            if (Image != null)
            {
                int iconX = btnRect.X + 6;
                int iconY = btnRect.Y + (btnRect.Height - Image.Height) / 2;
                g.DrawImage(Image, iconX, iconY, Image.Width, Image.Height);
            }

            // 5) Texte
            Color textColor = Enabled ? ForeColor : SystemColors.GrayText;

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                btnRect,
                textColor,
                TextFormatFlags.HorizontalCenter
              | TextFormatFlags.VerticalCenter
              | TextFormatFlags.EndEllipsis
              | TextFormatFlags.NoPrefix);

            // 6) Focus clavier
            if (Focused && ShowFocusCues)
            {
                var focusRect = new Rectangle(
                    btnRect.X + 3,
                    btnRect.Y + 3,
                    btnRect.Width - 6,
                    btnRect.Height - 6);

                ControlPaint.DrawFocusRectangle(g, focusRect, textColor, fill);
            }
        }

        // ----- Helpers -----

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();

            if (rect.Width <= 0 || rect.Height <= 0 || radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }

        private static Color LightenColor(Color color, float amount)
        {
            int r = Math.Min(255, (int)(color.R + (255 - color.R) * amount));
            int g = Math.Min(255, (int)(color.G + (255 - color.G) * amount));
            int b = Math.Min(255, (int)(color.B + (255 - color.B) * amount));
            return Color.FromArgb(color.A, r, g, b);
        }

        private static Color DarkenColor(Color color, float amount)
        {
            int r = Math.Max(0, (int)(color.R * (1 - amount)));
            int g = Math.Max(0, (int)(color.G * (1 - amount)));
            int b = Math.Max(0, (int)(color.B * (1 - amount)));
            return Color.FromArgb(color.A, r, g, b);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0)
                return;

            // La région est le rectangle du bouton SANS l'ombre (qui elle peut
            // dépasser légèrement). On inclut la zone d'ombre pour la voir.
            var regionRect = new Rectangle(0, 0, Width, Height);

            using var path = CreateRoundedPath(regionRect, _cornerRadius);
            Region = new Region(path);
        }        
    }
}