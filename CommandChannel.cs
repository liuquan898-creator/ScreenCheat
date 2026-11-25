using static ScreenCheat.NativeMethods;

namespace ScreenCheat;

/// <summary>
/// Lets a second launch (<c>--on</c>, <c>--off</c>, <c>--status</c>) control the running instance.
/// The running instance's hidden hotkey window answers a private message synchronously, so the
/// caller returns only after the change is on screen — a screenshot taken right after is correct.
/// </summary>
internal static class CommandChannel
{
    public const int Status = 0;
    public const int On = 1;
    public const int Off = 2;

    /// <summary>WM_APP + 1. wParam is the command; the reply is 1 if protection is on, 0 if off.</summary>
    public const int Message = 0x8001;

    /// <summary>Caption of the running instance's message-only window, used to find it.</summary>
    public const string WindowCaption = "ScreenCheat Hotkey";

    private const int TimeoutMs = 5000;

    /// <summary>Sends a command to the running instance. False if none answered.</summary>
    public static bool TrySend(int command, out bool active)
    {
        active = false;
        IntPtr window = FindWindowExW(HWND_MESSAGE, IntPtr.Zero, null, WindowCaption);
        if (window == IntPtr.Zero)
            return false;
        if (SendMessageTimeoutW(window, Message, new IntPtr(command), IntPtr.Zero, SMTO_ABORTIFHUNG, TimeoutMs, out IntPtr reply) == IntPtr.Zero)
            return false;
        if (reply != 0 && reply != 1)
            return false;
        active = reply == 1;
        return true;
    }
}
