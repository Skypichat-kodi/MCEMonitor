public class ToggleSwitch : Control
{
    private bool isOn = false;
    private int knobX = 2;
    private readonly System.Windows.Forms.Timer animationTimer;

    public bool Checked
    {
        get => isOn;
        set
        {
            isOn = value;
            StartAnimation();
            Invalidate();
        }
    }

    public ToggleSwitch()
    {
        // Activer la transparence
        this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        this.BackColor = Color.Transparent;

        // Anti-flicker
        this.DoubleBuffered = true;

        // Taille compacte
        this.Size = new Size(28, 14);

        this.Cursor = Cursors.Hand;

        // Timer d'animation
        animationTimer = new System.Windows.Forms.Timer();
        animationTimer.Interval = 10;
        animationTimer.Tick += Animate;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Fond
        Color backColor = isOn ? Color.FromArgb(76, 175, 80) : Color.FromArgb(200, 200, 200);
        using (SolidBrush brush = new SolidBrush(backColor))
        {
            e.Graphics.FillEllipse(brush, 0, 0, Height, Height);
            e.Graphics.FillEllipse(brush, Width - Height, 0, Height, Height);
            e.Graphics.FillRectangle(brush, Height / 2, 0, Width - Height, Height);
        }

        // Knob
        using (SolidBrush knobBrush = new SolidBrush(Color.White))
        {
            e.Graphics.FillEllipse(knobBrush, knobX, 2, Height - 4, Height - 4);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
    }

    private void StartAnimation()
    {
        animationTimer.Start();
    }

    private void Animate(object sender, EventArgs e)
    {
        int target = isOn ? Width - Height + 2 : 2; // fonctionne aussi pour 36×18

        if (isOn && knobX < target)
            knobX += 2;
        else if (!isOn && knobX > target)
            knobX -= 2;
        else
            animationTimer.Stop();

        Invalidate();
    }
}

