using System.ComponentModel;
using static ScreenCheat.NativeMethods;

namespace ScreenCheat;

/// <summary>
/// A full-monitor window the user sees straight through and clicks straight through,
/// but which every screenshot or recording tool sees as a solid black rectangle.
/// </summary>
/// <remarks>
/// The window is colour-keyed so it is invisible on the monitor, and marked with
/// <c>WDA_MONITOR</c> so the compositor replaces its whole rectangle with black in
/// captures. A window that is fully transparent through alpha 0 does not work: the
/// compositor drops it entirely and captures show the desktop again.
/// </remarks>
internal sealed class OverlayWindow : NativeWindow, IDisposable
{
    // Black is the colour key: the window surface starts out black, so the overlay
    // is see-through from its very first frame, before WM_PAINT has run.
    private static readonly Color KeyColor = Color.Black;
    private static readonly Color IndicatorColor = Color.FromArgb(229, 72, 77);

    private readonly Size _size;
    private readonly string? _indicatorText;

    /// <param name="bounds">Monitor bounds in physical pixels.</param>
    /// <param name="indicatorText">Badge text only the user can see; null draws nothing.</param>
    /// <param name="useAffinity">False only in tests that check the overlay is see-through.</param>
    public OverlayWindow(Rectangle bounds, string? indicatorText, bool useAffinity)
    {
        Bounds = bounds;
        _size = bounds.Size;
        _indicatorText = indicatorText;

        CreateHandle(new CreateParams
        {
            Caption = "ScreenCheat Overlay",
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
            Style = WS_POPUP,
            ExStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
        });

        try
        {
            if (!SetLayeredWindowAttributes(Handle, (uint)ColorTranslator.ToWin32(KeyColor), 0, LWA_COLORKEY))
                throw new Win32Exception();
            if (useAffinity && !SetWindowDisplayAffinity(Handle, WDA_MONITOR))
                throw new Win32Exception();
            if (!SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW))
                throw new Win32Exception();
        }
        catch
        {
            DestroyHandle();
            throw;
        }
    }

    /// <summary>Screen rectangle covered, in physical pixels.</summary>
    public Rectangle Bounds { get; }

    public void BringToTop()
    {
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
            DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_ERASEBKGND:
                m.Result = 1;
                return;
            case WM_PAINT:
                Paint();
                return;
            case WM_NCHITTEST:
                m.Result = HTTRANSPARENT;
                return;
        }
        base.WndProc(ref m);
    }

    private void Paint()
    {
        IntPtr hdc = BeginPaint(Handle, out PAINTSTRUCT paint);
        try
        {
            using Graphics g = Graphics.FromHdc(hdc);
            using var key = new SolidBrush(KeyColor);
            g.FillRectangle(key, 0, 0, _size.Width, _size.Height);
            if (_indicatorText is not null)
                DrawIndicator(g, _indicatorText);
        }
        finally
        {
            EndPaint(Handle, ref paint);
        }
    }

    /// <summary>A thin frame plus a badge at the top centre, both visible to the user only.</summary>
    private void DrawIndicator(Graphics g, string text)
    {
        float scale = GetDpiForWindow(Handle) / 96f;
        int width = _size.Width, height = _size.Height;
        int frame = Math.Max(2, (int)Math.Round(3 * scale));

        using var brush = new SolidBrush(IndicatorColor);
        g.FillRectangle(brush, 0, 0, width, frame);
        g.FillRectangle(brush, 0, height - frame, width, frame);
        g.FillRectangle(brush, 0, 0, frame, height);
        g.FillRectangle(brush, width - frame, 0, frame, height);

        const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        using var font = new Font("Microsoft YaHei UI", 13f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        Size textSize = TextRenderer.MeasureText(g, text, font, Size.Empty, Flags);
        int padX = (int)Math.Round(12 * scale), padY = (int)Math.Round(4 * scale);
        var badge = new Rectangle((width - textSize.Width) / 2 - padX, 0, textSize.Width + 2 * padX, textSize.Height + 2 * padY);
        g.FillRectangle(brush, badge);
        TextRenderer.DrawText(g, text, font, new Point(badge.X + padX, badge.Y + padY), Color.White, IndicatorColor, Flags);
    }
}
