using static ScreenCheat.NativeMethods;

namespace ScreenCheat;

/// <summary>
/// Invisible message-only window that receives the global hotkey and the
/// <see cref="CommandChannel"/> messages sent by a second launch.
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 1;
    private bool _registered;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Caption = CommandChannel.WindowCaption, Parent = HWND_MESSAGE });
    }

    public event Action? Pressed;

    /// <summary>Handles a <see cref="CommandChannel"/> command; returns 1 if protection is on afterwards, 0 if off.</summary>
    public Func<int, int>? CommandReceived { get; set; }

    /// <summary>Returns false when another program already owns the combination.</summary>
    public bool Register(KeyCombo combo)
    {
        Unregister();
        _registered = RegisterHotKey(Handle, HotkeyId, combo.Modifiers | MOD_NOREPEAT, (uint)combo.Key);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        if (Handle != IntPtr.Zero)
            DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && (int)m.WParam == HotkeyId)
            Pressed?.Invoke();
        if (m.Msg == CommandChannel.Message)
        {
            m.Result = new IntPtr(CommandReceived?.Invoke((int)m.WParam) ?? -1);
            return;
        }
        base.WndProc(ref m);
    }
}
