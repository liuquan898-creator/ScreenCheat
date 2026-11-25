using System.Text;
using static ScreenCheat.NativeMethods;

namespace ScreenCheat;

/// <summary>A global hotkey: RegisterHotKey modifier flags plus one key. Text form is "Ctrl+Alt+F9".</summary>
internal readonly record struct KeyCombo(uint Modifiers, Keys Key)
{
    public static readonly KeyCombo Default = new(MOD_CONTROL | MOD_ALT, Keys.F9);

    /// <summary>
    /// True when the combination is safe to grab system-wide: a real key, and either a
    /// modifier or a function key, so plain typing is never swallowed.
    /// </summary>
    public bool IsUsable =>
        Key != Keys.None && !IsModifierKey(Key) && (Modifiers != 0 || Key is >= Keys.F1 and <= Keys.F24);

    public static bool TryParse(string? text, out KeyCombo combo)
    {
        combo = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        uint modifiers = 0;
        Keys key = Keys.None;
        foreach (string part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= MOD_ALT;
                    break;
                case "shift":
                    modifiers |= MOD_SHIFT;
                    break;
                case "win" or "windows":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    if (key != Keys.None)
                        return false; // two non-modifier keys
                    if (part.Length == 1 && char.IsAsciiDigit(part[0]))
                        key = Keys.D0 + (part[0] - '0');
                    else if (part.All(char.IsAsciiDigit) || !Enum.TryParse(part, ignoreCase: true, out key))
                        return false;
                    break;
            }
        }

        combo = new KeyCombo(modifiers, key);
        return combo.IsUsable;
    }

    /// <summary>Builds a combo from WinForms key data (key code plus Ctrl/Alt/Shift flags).</summary>
    public static KeyCombo FromKeyData(Keys keyData)
    {
        uint modifiers = 0;
        if ((keyData & Keys.Control) != 0) modifiers |= MOD_CONTROL;
        if ((keyData & Keys.Alt) != 0) modifiers |= MOD_ALT;
        if ((keyData & Keys.Shift) != 0) modifiers |= MOD_SHIFT;
        return new KeyCombo(modifiers, keyData & Keys.KeyCode);
    }

    public override string ToString()
    {
        var text = new StringBuilder();
        if ((Modifiers & MOD_CONTROL) != 0) text.Append("Ctrl+");
        if ((Modifiers & MOD_ALT) != 0) text.Append("Alt+");
        if ((Modifiers & MOD_SHIFT) != 0) text.Append("Shift+");
        if ((Modifiers & MOD_WIN) != 0) text.Append("Win+");
        text.Append(Key is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (Key - Keys.D0))).ToString() : Key.ToString());
        return text.ToString();
    }

    private static bool IsModifierKey(Keys key) => key is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.LWin or Keys.RWin;
}
