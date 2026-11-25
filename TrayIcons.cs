using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ScreenCheat;

internal static class TrayIcons
{
    /// <summary>Draws the tray icon: a blue monitor when idle, a blacked-out monitor with a red slash when active.</summary>
    public static Icon Create(bool active)
    {
        Color frame = active ? Color.FromArgb(229, 72, 77) : Color.FromArgb(235, 238, 243);
        Color screen = active ? Color.FromArgb(20, 20, 22) : Color.FromArgb(58, 124, 220);

        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var display = new Rectangle(3, 5, 26, 18);
            using var screenBrush = new SolidBrush(screen);
            using var frameBrush = new SolidBrush(frame);
            using var framePen = new Pen(frame, 3f);
            g.FillRectangle(screenBrush, display);
            g.DrawRectangle(framePen, display);
            g.FillRectangle(frameBrush, 14, 24, 4, 3);
            g.FillRectangle(frameBrush, 9, 27, 14, 2);
            if (active)
                g.DrawLine(framePen, 7, 20, 25, 8);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
