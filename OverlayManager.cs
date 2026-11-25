using static ScreenCheat.NativeMethods;

namespace ScreenCheat;

/// <summary>
/// Owns the overlay windows and keeps them above every other topmost window.
/// Anything that ends up above an overlay is visible to capture tools, so a
/// newly shown topmost window (menu, tooltip, dropdown) or a foreground change
/// is answered at once by raising the overlays again. A window that makes itself
/// topmost without being shown or activated raises no event, so a short timer
/// also checks whether anything visible sits above an overlay.
/// </summary>
internal sealed class OverlayManager : IDisposable
{
    private const int MinRaiseIntervalMs = 15;
    private const int WatchIntervalMs = 30;

    // Windows in a higher system band (IME, Start menu) stay above us whatever we do.
    // They are remembered so the watch does not raise for them on every tick, and
    // forgotten now and then in case a handle is reused by an ordinary window.
    private const int ForgetUnbeatableMs = 5000;

    private readonly List<OverlayWindow> _overlays = [];
    private readonly HashSet<IntPtr> _unbeatable = [];
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = WatchIntervalMs };
    private readonly System.Windows.Forms.Timer _deferredRaise = new() { Interval = MinRaiseIntervalMs };
    private readonly WinEventProc _winEventCallback; // field keeps the delegate alive while hooked
    private IntPtr _showHook;
    private IntPtr _foregroundHook;
    private long _lastRaise;
    private long _unbeatableSince;

    public OverlayManager()
    {
        _winEventCallback = OnWinEvent;
        _watch.Tick += (_, _) => Watch();
        _deferredRaise.Tick += (_, _) => Raise();
    }

    public bool Active => _overlays.Count > 0;

    /// <summary>
    /// Another window of ours (the pet) to keep above other apps' topmost windows,
    /// just below the overlays. Zero for none.
    /// </summary>
    public IntPtr Companion { get; set; }

    /// <summary>Replaces the current overlays with one per area. Throws if the system refuses.</summary>
    public void Show(IReadOnlyList<Rectangle> areas, string? indicatorText, bool useAffinity)
    {
        Hide();
        try
        {
            foreach (Rectangle area in areas)
                _overlays.Add(new OverlayWindow(area, indicatorText, useAffinity));
        }
        catch
        {
            Hide();
            throw;
        }

        if (!Active)
            return;
        _showHook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _winEventCallback, 0, 0, WINEVENT_OUTOFCONTEXT);
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEventCallback, 0, 0, WINEVENT_OUTOFCONTEXT);
        _unbeatableSince = Environment.TickCount64;
        _watch.Start();
    }

    public void Hide()
    {
        _watch.Stop();
        _deferredRaise.Stop();
        if (_showHook != IntPtr.Zero)
        {
            UnhookWinEvent(_showHook);
            _showHook = IntPtr.Zero;
        }
        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
        foreach (OverlayWindow overlay in _overlays)
            overlay.Dispose();
        _overlays.Clear();
        _unbeatable.Clear();
    }

    public void Dispose()
    {
        Hide();
        _watch.Dispose();
        _deferredRaise.Dispose();
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != 0 || IsOverlay(hwnd))
            return;
        if (evt == EVENT_OBJECT_SHOW)
        {
            // Only a top-level topmost window can land above the overlays.
            if (GetAncestor(hwnd, GA_ROOT) != hwnd || (GetWindowLongW(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0)
                return;
        }

        // Rate-limit so another always-on-top keeper cannot drag us into a tight loop.
        if (Environment.TickCount64 - _lastRaise >= MinRaiseIntervalMs)
            Raise();
        else
            _deferredRaise.Start();
    }

    private void Watch()
    {
        if (Environment.TickCount64 - _unbeatableSince >= ForgetUnbeatableMs)
        {
            _unbeatable.Clear();
            _unbeatableSince = Environment.TickCount64;
        }
        if (FindWindowsAbove().Count > 0)
            Raise();
    }

    private void Raise()
    {
        _deferredRaise.Stop();
        _lastRaise = Environment.TickCount64;
        if (Companion != IntPtr.Zero)
            SetWindowPos(Companion, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        foreach (OverlayWindow overlay in _overlays)
            overlay.BringToTop();

        // Whatever is still above us right after raising lives in a band we cannot reach.
        _unbeatable.UnionWith(FindWindowsAbove());
    }

    /// <summary>Visible windows above an overlay that overlap it, other than known unbeatable ones.</summary>
    private List<IntPtr> FindWindowsAbove()
    {
        var found = new List<IntPtr>();
        foreach (OverlayWindow overlay in _overlays)
        {
            for (IntPtr h = GetWindow(overlay.Handle, GW_HWNDPREV); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDPREV))
            {
                if (IsOverlay(h) || _unbeatable.Contains(h) || !IsWindowVisible(h))
                    continue;
                if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    continue;
                if (!GetWindowRect(h, out RECT r) || r.Right <= r.Left || r.Bottom <= r.Top)
                    continue;
                if (overlay.Bounds.IntersectsWith(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)))
                    found.Add(h);
            }
        }
        return found;
    }

    private bool IsOverlay(IntPtr hwnd)
    {
        if (hwnd == Companion && hwnd != IntPtr.Zero)
            return true;
        foreach (OverlayWindow overlay in _overlays)
        {
            if (overlay.Handle == hwnd)
                return true;
        }
        return false;
    }
}
