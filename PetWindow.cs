using System.ComponentModel;
using System.Drawing.Imaging;
using static ScreenCheat.NativeMethods;

namespace ScreenCheat;

/// <summary>
/// The desktop pet shown while protection is on: a small always-on-top, per-pixel-alpha
/// window that plays <see cref="PetArt"/>. Drag it with the left button; the right button
/// asks for the app menu. It never takes focus.
/// </summary>
internal sealed class PetWindow : NativeWindow, IDisposable
{
    private const int FrameMs = 66;   // ~15 fps is plenty for a small idle animation
    private const int MarginLogical = 16;

    private readonly System.Windows.Forms.Timer _frames = new() { Interval = FrameMs };
    private readonly long _start = Environment.TickCount64;
    private Size _size;
    private float _scale;
    private IntPtr _memDc, _dib, _oldBitmap;
    private Bitmap? _surface;
    private Graphics? _graphics;
    private bool _dragging;
    private Point _dragCursorStart, _dragWindowStart;

    public PetWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "ScreenCheat Pet",
            Style = WS_POPUP,
            ExStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
            Width = 1,
            Height = 1,
        });
        _frames.Tick += (_, _) => Render();
    }

    /// <summary>Raised after the user drops the pet somewhere new (top-left, physical pixels).</summary>
    public event Action<Point>? Moved;

    /// <summary>Raised on right-click.</summary>
    public event Action? MenuRequested;

    public bool Visible { get; private set; }

    public Point Location => GetWindowRect(Handle, out RECT r) ? new Point(r.Left, r.Top) : Point.Empty;

    /// <summary>Shows the pet at a saved position, or bottom-right of the primary screen if that is null or off-screen.</summary>
    public void Show(Point? savedTopLeft)
    {
        Point target;
        if (savedTopLeft is Point saved && IsOnScreen(saved))
        {
            target = saved;
            MoveTo(target);
            UpdateScale();
        }
        else
        {
            // Land on the primary screen first so the size is computed for its DPI.
            Rectangle work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
            MoveTo(work.Location);
            UpdateScale();
            int margin = (int)(MarginLogical * _scale);
            target = new Point(work.Right - _size.Width - margin, work.Bottom - _size.Height - margin);
            MoveTo(target);
        }

        Render();
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Visible = true;
        _frames.Start();
    }

    public void Hide()
    {
        _frames.Stop();
        EndDrag();
        if (Visible)
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);
        Visible = false;
    }

    public void Dispose()
    {
        Hide();
        _frames.Dispose();
        FreeSurface();
        if (Handle != IntPtr.Zero)
            DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE:
                m.Result = MA_NOACTIVATE;
                return;
            case WM_LBUTTONDOWN:
                _dragging = true;
                _dragCursorStart = ToScreen(m.LParam);
                _dragWindowStart = Location;
                SetCapture(Handle);
                return;
            case WM_MOUSEMOVE:
                if (_dragging)
                {
                    Point cursor = ToScreen(m.LParam);
                    MoveTo(new Point(_dragWindowStart.X + cursor.X - _dragCursorStart.X, _dragWindowStart.Y + cursor.Y - _dragCursorStart.Y));
                }
                return;
            case WM_LBUTTONUP:
                if (_dragging)
                {
                    EndDrag();
                    Moved?.Invoke(Location);
                }
                return;
            case WM_CAPTURECHANGED:
                _dragging = false;
                return;
            case WM_RBUTTONUP:
                MenuRequested?.Invoke();
                return;
            case WM_DPICHANGED:
                // Dragged onto a monitor with a different scale: redraw at the new size.
                UpdateScale();
                if (Visible)
                    Render();
                m.Result = IntPtr.Zero;
                return;
        }
        base.WndProc(ref m);
    }

    private void EndDrag()
    {
        if (_dragging)
        {
            _dragging = false;
            ReleaseCapture();
        }
    }

    /// <summary>Client coordinates in a mouse message's lParam, converted to screen coordinates.</summary>
    private Point ToScreen(IntPtr lParam)
    {
        long value = lParam.ToInt64();
        var point = new POINT { X = (short)(value & 0xFFFF), Y = (short)((value >> 16) & 0xFFFF) };
        ClientToScreen(Handle, ref point);
        return new Point(point.X, point.Y);
    }

    private void MoveTo(Point topLeft)
    {
        SetWindowPos(Handle, IntPtr.Zero, topLeft.X, topLeft.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private static bool IsOnScreen(Point topLeft)
    {
        // Require a decent chunk of the pet to be on some screen, so it can always be grabbed.
        var probe = new Rectangle(topLeft.X + 20, topLeft.Y + 20, 40, 40);
        return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(probe));
    }

    private void UpdateScale()
    {
        float scale = GetDpiForWindow(Handle) / 96f;
        if (scale <= 0)
            scale = 1f;
        var size = new Size((int)Math.Ceiling(PetArt.Width * scale), (int)Math.Ceiling(PetArt.Height * scale));
        if (scale == _scale && size == _size && _surface is not null)
            return;
        _scale = scale;
        _size = size;
        CreateSurface();
    }

    /// <summary>A premultiplied 32-bit DIB that GDI+ draws into and UpdateLayeredWindow reads from.</summary>
    private void CreateSurface()
    {
        FreeSurface();
        IntPtr screen = GetDC(IntPtr.Zero);
        try
        {
            _memDc = CreateCompatibleDC(screen);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = _size.Width,
            biHeight = -_size.Height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        _dib = CreateDIBSection(_memDc, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (_dib == IntPtr.Zero)
            throw new Win32Exception("CreateDIBSection failed");
        _oldBitmap = SelectObject(_memDc, _dib);

        _surface = new Bitmap(_size.Width, _size.Height, _size.Width * 4, PixelFormat.Format32bppPArgb, bits);
        _graphics = Graphics.FromImage(_surface);
        _graphics.ScaleTransform(_scale, _scale);
    }

    private void FreeSurface()
    {
        _graphics?.Dispose();
        _graphics = null;
        _surface?.Dispose();
        _surface = null;
        if (_memDc != IntPtr.Zero)
        {
            SelectObject(_memDc, _oldBitmap);
            DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        if (_dib != IntPtr.Zero)
        {
            DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }
    }

    private void Render()
    {
        if (_graphics is null)
            return;
        _graphics.Clear(Color.Transparent);
        PetArt.Draw(_graphics, (Environment.TickCount64 - _start) / 1000f);
        _graphics.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);

        var size = new SIZE { Width = _size.Width, Height = _size.Height };
        var source = new POINT();
        var blend = new BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(Handle, IntPtr.Zero, IntPtr.Zero, ref size, _memDc, ref source, 0, ref blend, ULW_ALPHA);
    }
}
