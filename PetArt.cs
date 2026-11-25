using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ScreenCheat;

/// <summary>
/// Draws one frame of the desktop pet: a little hooded hacker typing on a laptop,
/// with a floating terminal and bits of data flying from the laptop into it.
/// Everything is in logical units (Width × Height); the caller scales.
/// </summary>
internal static class PetArt
{
    public const int Width = 150;
    public const int Height = 140;

    private static readonly Color Neon = Color.FromArgb(70, 255, 150);
    private static readonly Color Hoodie = Color.FromArgb(46, 50, 63);
    private static readonly Color HoodieLight = Color.FromArgb(70, 76, 94);
    private static readonly Color HoodieInner = Color.FromArgb(26, 28, 36);
    private static readonly Color Face = Color.FromArgb(11, 13, 17);
    private static readonly Color LidTop = Color.FromArgb(76, 81, 96);
    private static readonly Color LidBottom = Color.FromArgb(50, 54, 66);
    private static readonly Color Keyboard = Color.FromArgb(92, 98, 115);

    private static readonly Font CodeFont = new("Consolas", 6.2f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font TitleFont = new("Consolas", 5.2f, FontStyle.Bold, GraphicsUnit.Pixel);
    private static readonly Font LogoFont = new("Consolas", 10f, FontStyle.Bold, GraphicsUnit.Pixel);
    private static readonly Font BitFont = new("Consolas", 7.5f, FontStyle.Bold, GraphicsUnit.Pixel);

    // At most 13 characters: that is what fits across the terminal.
    private static readonly string[] CodeLines =
    [
        "$ ssh ghost",
        "> cloak -all",
        "0101 1100 01",
        "inject(0x1F);",
        "[OK] rec=0",
        "while(1)hide",
        "0xDEADBEEF",
        "> trace -rm",
        "1010 0110 10",
        "chmod +x cat",
        "[OK] stealth",
        "> ping 127.1",
    ];

    // Floating terminal, in logical units.
    private static readonly RectangleF Panel = new(95, 6, 52, 56);
    private const float CodeTop = 19, CodeLeft = 98, LineHeight = 7.4f, ScrollSpeed = 8f;

    public static void Draw(Graphics g, float t)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float bob = MathF.Sin(t * 2.2f) * 1.2f;

        DrawShadow(g);
        DrawBeam(g, t);
        DrawPanel(g, t);

        GraphicsState state = g.Save();
        g.TranslateTransform(0, bob);
        DrawBody(g);
        DrawHead(g, t);
        g.Restore(state);

        float left = Typing(t, 0f), right = Typing(t, MathF.PI);
        DrawArms(g, bob, left, right);
        DrawLaptop(g, t);
        DrawPaw(g, 25, 113 + left);
        DrawPaw(g, 93, 113 + right);
        DrawBits(g, t);
    }

    /// <summary>Paw lift while typing: quick taps, alternating between the two paws.</summary>
    private static float Typing(float t, float phase) => -3.5f * MathF.Max(0, MathF.Sin(t * 15f + phase));

    private static void DrawShadow(Graphics g)
    {
        using var brush = new SolidBrush(Color.FromArgb(60, 0, 0, 0));
        g.FillEllipse(brush, 12, 125, 94, 11);
    }

    /// <summary>A faint projection cone from the laptop to the floating terminal.</summary>
    private static void DrawBeam(Graphics g, float t)
    {
        int alpha = 16 + (int)(8 * MathF.Sin(t * 3f));
        using var brush = new SolidBrush(Color.FromArgb(alpha, Neon));
        g.FillPolygon(brush, [new PointF(82, 90), new PointF(89, 90), new PointF(Panel.Right - 14, Panel.Bottom), new PointF(Panel.Left + 4, Panel.Bottom)]);
    }

    private static void DrawPanel(Graphics g, float t)
    {
        using (GraphicsPath glowPath = RoundedRect(RectangleF.Inflate(Panel, 1.5f, 1.5f), 6))
        using (var glow = new Pen(Color.FromArgb(40, Neon), 3f))
            g.DrawPath(glow, glowPath);

        using GraphicsPath path = RoundedRect(Panel, 5);
        using (var fill = new SolidBrush(Color.FromArgb(220, 6, 26, 15)))
            g.FillPath(fill, path);
        using (var border = new Pen(Color.FromArgb(180, Neon), 1f))
            g.DrawPath(border, path);

        // Title bar: window dots and a label.
        Color[] dots = [Color.FromArgb(255, 95, 86), Color.FromArgb(255, 189, 46), Color.FromArgb(39, 201, 63)];
        for (int i = 0; i < dots.Length; i++)
        {
            using var dot = new SolidBrush(Color.FromArgb(210, dots[i]));
            g.FillEllipse(dot, Panel.Left + 4 + i * 4.6f, Panel.Top + 4, 3.2f, 3.2f);
        }
        using (var title = new SolidBrush(Color.FromArgb(200, Neon)))
            g.DrawString("STEALTH", TitleFont, title, Panel.Left + 19, Panel.Top + 2.6f);
        using (var rule = new Pen(Color.FromArgb(70, Neon), 0.6f))
            g.DrawLine(rule, Panel.Left + 2, Panel.Top + 10.5f, Panel.Right - 2, Panel.Top + 10.5f);

        // Code scrolls up; the newest line at the bottom types itself in.
        GraphicsState state = g.Save();
        g.SetClip(RectangleF.FromLTRB(Panel.Left + 2, Panel.Top + 11.5f, Panel.Right - 2, Panel.Bottom - 2));
        float scrolled = t * ScrollSpeed;
        int first = (int)(scrolled / LineHeight);
        float offset = scrolled % LineHeight;
        float typed = offset / LineHeight;
        const int Visible = 6;
        using (var text = new SolidBrush(Color.FromArgb(225, Neon)))
        using (var dim = new SolidBrush(Color.FromArgb(120, Neon)))
        {
            for (int i = 0; i <= Visible; i++)
            {
                string line = CodeLines[(first + i) % CodeLines.Length];
                float y = CodeTop + i * LineHeight - offset;
                bool newest = i == Visible;
                if (newest)
                {
                    line = line[..Math.Max(0, (int)(line.Length * typed))];
                    if ((t * 2.5f) % 1f < 0.6f)
                    {
                        SizeF size = g.MeasureString(line, CodeFont, PointF.Empty, StringFormat.GenericTypographic);
                        g.FillRectangle(text, CodeLeft + size.Width + 0.6f, y + 0.8f, 3f, 5.2f);
                    }
                }
                g.DrawString(line, CodeFont, i < 2 ? dim : text, CodeLeft, y, StringFormat.GenericTypographic);
            }
        }
        g.Restore(state);
    }

    private static void DrawBody(Graphics g)
    {
        using (var body = new LinearGradientBrush(new RectangleF(20, 64, 76, 70), HoodieLight, Hoodie, LinearGradientMode.Vertical))
            g.FillEllipse(body, 21, 66, 74, 68);

        // Hoodie strings with little metal tips.
        using var cord = new Pen(Color.FromArgb(165, 170, 186), 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(cord, 51.5f, 80, 50.5f, 91);
        g.DrawLine(cord, 64.5f, 80, 65.5f, 91);
        using var tip = new SolidBrush(Color.FromArgb(200, 204, 214));
        g.FillEllipse(tip, 49.2f, 90, 2.6f, 3.4f);
        g.FillEllipse(tip, 64.2f, 90, 2.6f, 3.4f);
    }

    private static void DrawHead(Graphics g, float t)
    {
        // Cat-ear hood: ears first so the head covers their base.
        using (var hood = new SolidBrush(Hoodie))
        {
            g.FillPolygon(hood, [new PointF(31, 40), new PointF(35, 13), new PointF(53, 27)]);
            g.FillPolygon(hood, [new PointF(63, 27), new PointF(81, 13), new PointF(85, 40)]);
        }
        using (var inner = new SolidBrush(HoodieInner))
        {
            g.FillPolygon(inner, [new PointF(36, 33), new PointF(38, 19.5f), new PointF(47, 27.5f)]);
            g.FillPolygon(inner, [new PointF(69, 27.5f), new PointF(78, 19.5f), new PointF(80, 33)]);
        }
        using (var head = new LinearGradientBrush(new RectangleF(27, 21, 62, 62), HoodieLight, Hoodie, LinearGradientMode.Vertical))
            g.FillEllipse(head, 28, 22, 60, 60);

        // Face opening, rimmed, lit green from below by the laptop screen.
        using (var rim = new SolidBrush(HoodieInner))
            g.FillEllipse(rim, 36, 38, 44, 36);
        using (var face = new SolidBrush(Face))
            g.FillEllipse(face, 38.5f, 40.5f, 39, 31);
        using (var lit = new SolidBrush(Color.FromArgb(34, Neon)))
            g.FillEllipse(lit, 41, 57, 34, 15);

        // Eyes: glow, blink every few seconds, glance up at the terminal now and then.
        float cycle = t % 7f;
        float look = cycle < 4f ? 0 : cycle < 4.4f ? (cycle - 4f) / 0.4f : cycle < 6f ? 1 : cycle < 6.4f ? 1 - (cycle - 6f) / 0.4f : 0;
        float lx = look * 2.4f, ly = -look * 1.4f;
        bool blink = t % 3.4f < 0.14f;
        float eyeH = blink ? 1.4f : 7f;
        float eyeY = 51.5f + ly + (7f - eyeH) / 2;
        foreach (float ex in new[] { 45.5f + lx, 61.5f + lx })
        {
            using (GraphicsPath glowPath = RoundedRect(new RectangleF(ex - 2.5f, eyeY - 2.5f, 14, eyeH + 5), 4))
            using (var glow = new SolidBrush(Color.FromArgb(55, Neon)))
                g.FillPath(glow, glowPath);
            using GraphicsPath eye = RoundedRect(new RectangleF(ex, eyeY, 9, eyeH), Math.Min(2.5f, eyeH / 2));
            using var bright = new SolidBrush(Neon);
            g.FillPath(bright, eye);
        }

        // A small smirk.
        using var mouth = new Pen(Color.FromArgb(150, Neon), 1.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(mouth, 54.5f + lx * 0.4f, 60.5f, 8, 5, 25, 120);
    }

    private static void DrawArms(Graphics g, float bob, float left, float right)
    {
        using var arm = new Pen(Hoodie, 7.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(arm, 31, 92 + bob, 26, 111 + left);
        g.DrawLine(arm, 85, 92 + bob, 92, 111 + right);
    }

    private static void DrawLaptop(Graphics g, float t)
    {
        // Lid, seen from behind, with a pulsing logo.
        var lid = new RectangleF(30, 86, 58, 32);
        using (GraphicsPath lidPath = RoundedRect(lid, 3.5f))
        using (var lidBrush = new LinearGradientBrush(lid, LidTop, LidBottom, LinearGradientMode.Vertical))
        {
            g.FillPath(lidBrush, lidPath);
            using var edge = new Pen(Color.FromArgb(120, 150, 158, 178), 0.8f);
            g.DrawPath(edge, lidPath);
        }
        int pulse = 150 + (int)(90 * MathF.Sin(t * 3f));
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using (var logoGlow = new SolidBrush(Color.FromArgb(pulse / 4, Neon)))
            g.FillEllipse(logoGlow, 47, 94, 24, 16);
        using (var logo = new SolidBrush(Color.FromArgb(Math.Clamp(pulse + 20, 0, 255), Neon)))
            g.DrawString("</>", LogoFont, logo, new RectangleF(lid.X, lid.Y + 1, lid.Width, lid.Height), format);

        // Keyboard base.
        using var baseBrush = new SolidBrush(Keyboard);
        g.FillPolygon(baseBrush, [new PointF(22, 117.5f), new PointF(96, 117.5f), new PointF(101, 123.5f), new PointF(17, 123.5f)]);
        using var front = new Pen(Color.FromArgb(60, 64, 78), 1.2f);
        g.DrawLine(front, 17.5f, 123.5f, 100.5f, 123.5f);
    }

    private static void DrawPaw(Graphics g, float cx, float cy)
    {
        using (var paw = new SolidBrush(HoodieLight))
            g.FillEllipse(paw, cx - 6.5f, cy - 4.5f, 13, 9.5f);
        using var pad = new SolidBrush(Color.FromArgb(95, 102, 122));
        g.FillEllipse(pad, cx - 3.5f, cy - 2f, 7, 4.5f);
    }

    /// <summary>Bits of data flying from the laptop up into the terminal.</summary>
    private static void DrawBits(Graphics g, float t)
    {
        const int Count = 7;
        const float Period = 2.2f;
        for (int i = 0; i < Count; i++)
        {
            float local = t + i * (Period / Count);
            float p = local % Period / Period;
            int generation = (int)(local / Period);
            float x0 = 70 + i * 7 % 15, x1 = 100 + i * 11 % 34;
            float x = x0 + (x1 - x0) * p;
            float y = 86 - 26 * p - 10 * MathF.Sin(p * MathF.PI);
            int alpha = (int)(230 * (1 - p) * MathF.Min(1, p * 6));
            if (alpha <= 0)
                continue;
            using var brush = new SolidBrush(Color.FromArgb(alpha, Neon));
            g.DrawString((i + generation) % 2 == 0 ? "1" : "0", BitFont, brush, x, y, StringFormat.GenericTypographic);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
