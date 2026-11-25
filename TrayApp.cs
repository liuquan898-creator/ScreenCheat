using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ScreenCheat;

/// <summary>The tray icon, its menu, the global hotkey and the on/off state.</summary>
internal sealed partial class TrayApp : ApplicationContext
{
    private const string AppName = "ScreenCheat";

    private readonly Settings _settings = Settings.Load();
    private readonly bool _useAffinity;
    private readonly OverlayManager _overlays = new();
    private readonly HotkeyWindow _hotkeyWindow = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly Icon _iconOff = TrayIcons.Create(active: false);
    private readonly Icon _iconOn = TrayIcons.Create(active: true);
    private readonly NotifyIcon _tray;
    private readonly SynchronizationContext _ui;
    private KeyCombo _hotkey;
    private bool _hotkeyRegistered;
    private PetWindow? _pet;

    /// <param name="startEnabled">Turn protection on immediately; null follows the EnableOnStart setting.</param>
    /// <param name="useAffinity">False only in tests that check the overlay is see-through.</param>
    public TrayApp(bool? startEnabled, bool useAffinity)
    {
        _useAffinity = useAffinity;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        if (!KeyCombo.TryParse(_settings.Hotkey, out _hotkey))
            _hotkey = KeyCombo.Default;

        _menu.Opening += (_, e) =>
        {
            BuildMenu();
            e.Cancel = false;
        };
        _tray = new NotifyIcon { Icon = _iconOff, ContextMenuStrip = _menu, Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                Toggle();
        };

        _hotkeyWindow.Pressed += Toggle;
        _hotkeyRegistered = _hotkeyWindow.Register(_hotkey);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        if (startEnabled ?? _settings.EnableOnStart)
            Enable();
        UpdateTray();

        // No notification while protection is on: toasts sit above the overlays and would be recorded.
        if (!_overlays.Active)
        {
            _tray.ShowBalloonTip(4000, AppName, _hotkeyRegistered
                ? $"已在托盘运行。按 {_hotkey} 或单击托盘图标开关防录屏。"
                : $"已在托盘运行。热键 {_hotkey} 被其他程序占用，请在托盘菜单里修改。", ToolTipIcon.Info);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _overlays.Dispose();
            _pet?.Dispose();
            _hotkeyWindow.Dispose();
            _tray.Dispose();
            _menu.Dispose();
            _iconOn.Dispose();
            _iconOff.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Toggle()
    {
        if (_overlays.Active)
        {
            _overlays.Hide();
            HidePet();
        }
        else
        {
            Enable();
        }
        UpdateTray();
    }

    private void Enable()
    {
        // Pet first, so the overlays created next sit above it.
        ShowOrHidePet();
        try
        {
            _overlays.Show(SelectedScreens().Select(screen => screen.Bounds).ToList(), _useAffinity);
        }
        catch (Win32Exception e)
        {
            HidePet();
            MessageBox.Show($"无法开启防录屏：{e.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowOrHidePet()
    {
        if (!_settings.ShowPet)
        {
            HidePet();
            return;
        }
        try
        {
            if (_pet is null)
            {
                _pet = new PetWindow();
                _pet.Moved += location =>
                {
                    _settings.PetX = location.X;
                    _settings.PetY = location.Y;
                    _settings.Save();
                };
                _pet.MenuRequested += ShowMenuAtCursor;
            }
            Point? saved = _settings.PetX is int x && _settings.PetY is int y ? new Point(x, y) : null;
            _pet.Show(saved);
            _overlays.Companion = _pet.Handle;
        }
        catch (Win32Exception)
        {
            // The pet is only a status hint; protection works without it.
            HidePet();
        }
    }

    private void HidePet()
    {
        _pet?.Hide();
        _overlays.Companion = IntPtr.Zero;
    }

    private void ShowMenuAtCursor()
    {
        // Our process just got the right-click, so it may take the foreground; without
        // that, the menu would not close when the user clicks somewhere else.
        if (_pet is not null)
            NativeMethods.SetForegroundWindow(_pet.Handle);
        _menu.Show(Cursor.Position);
    }

    /// <summary>Re-creates the overlays after a settings or display change, if protection is on.</summary>
    private void RefreshOverlays()
    {
        if (_overlays.Active)
            Enable();
        UpdateTray();
    }

    private void UpdateTray()
    {
        _tray.Icon = _overlays.Active ? _iconOn : _iconOff;
        _tray.Text = _overlays.Active
            ? $"{AppName}：防录屏已开启"
            : _hotkeyRegistered ? $"{AppName}：未开启（{_hotkey} 开启）" : $"{AppName}：未开启";
    }

    /// <summary>The monitors to cover. A saved selection that matches nothing falls back to all monitors.</summary>
    private List<Screen> SelectedScreens()
    {
        Screen[] all = Screen.AllScreens;
        if (_settings.Monitors.Count == 0)
            return [.. all];
        List<Screen> selected = all.Where(screen => _settings.Monitors.Contains(screen.DeviceName)).ToList();
        return selected.Count > 0 ? selected : [.. all];
    }

    private void BuildMenu()
    {
        for (int i = _menu.Items.Count - 1; i >= 0; i--)
        {
            ToolStripItem old = _menu.Items[i];
            _menu.Items.RemoveAt(i);
            old.Dispose();
        }

        var toggle = new ToolStripMenuItem(_overlays.Active ? "关闭防录屏" : "开启防录屏", null, (_, _) => Toggle());
        if (_hotkeyRegistered)
            toggle.ShortcutKeyDisplayString = _hotkey.ToString();
        _menu.Items.Add(toggle);
        _menu.Items.Add(new ToolStripSeparator());

        bool coversAll = _settings.Monitors.Count == 0;
        var range = new ToolStripMenuItem("遮挡范围");
        range.DropDownItems.Add(new ToolStripMenuItem("全部显示器", null, (_, _) => SetMonitors([])) { Checked = coversAll });
        range.DropDownItems.Add(new ToolStripSeparator());
        foreach (Screen screen in Screen.AllScreens.OrderBy(DisplayNumber))
        {
            string device = screen.DeviceName;
            string label = $"显示器 {DisplayNumber(screen)} · {screen.Bounds.Width}×{screen.Bounds.Height}{(screen.Primary ? " · 主显示器" : "")}";
            range.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) => ToggleMonitor(device))
            {
                Checked = !coversAll && _settings.Monitors.Contains(device),
            });
        }
        _menu.Items.Add(range);

        _menu.Items.Add(new ToolStripMenuItem("显示桌面宠物", null, (_, _) =>
        {
            _settings.ShowPet = !_settings.ShowPet;
            _settings.Save();
            RefreshOverlays();
        })
        {
            Checked = _settings.ShowPet,
        });
        if (_settings.ShowPet && _settings.PetX is not null)
        {
            _menu.Items.Add(new ToolStripMenuItem("宠物回到右下角", null, (_, _) =>
            {
                _settings.PetX = null;
                _settings.PetY = null;
                _settings.Save();
                RefreshOverlays();
            }));
        }
        _menu.Items.Add(new ToolStripMenuItem("启动时自动开启", null, (_, _) =>
        {
            _settings.EnableOnStart = !_settings.EnableOnStart;
            _settings.Save();
        })
        {
            Checked = _settings.EnableOnStart,
        });
        _menu.Items.Add(new ToolStripMenuItem("修改热键…", null, (_, _) => ChangeHotkey()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => Exit()));
    }

    private void ToggleMonitor(string device)
    {
        List<string> connected = Screen.AllScreens.Select(screen => screen.DeviceName).ToList();
        // From "all monitors", clicking one monitor narrows the selection to just that one.
        List<string> selected = _settings.Monitors.Where(connected.Contains).ToList();
        if (!selected.Remove(device))
            selected.Add(device);
        if (selected.Count == connected.Count)
            selected.Clear();
        SetMonitors(selected);
    }

    private void SetMonitors(List<string> devices)
    {
        _settings.Monitors = devices;
        _settings.Save();
        RefreshOverlays();
    }

    private void ChangeHotkey()
    {
        // Release the hotkey first so pressing the current combination lands in the dialog.
        _hotkeyWindow.Unregister();
        using (var dialog = new HotkeyDialog(_hotkey))
        {
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _hotkey = dialog.Result;
                _settings.Hotkey = _hotkey.ToString();
                _settings.Save();
            }
        }

        _hotkeyRegistered = _hotkeyWindow.Register(_hotkey);
        if (!_hotkeyRegistered)
        {
            MessageBox.Show($"热键 {_hotkey} 已被其他程序占用，请换一个。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        UpdateTray(); // the tooltip shows the hotkey
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _ui.Post(_ => RefreshOverlays(), null);
    }

    /// <summary>Thread-safe: quits the app from any thread.</summary>
    public void RequestExit() => _ui.Post(_ => Exit(), null);

    private void Exit()
    {
        _overlays.Hide();
        HidePet();
        _tray.Visible = false;
        ExitThread();
    }

    /// <summary>The number Windows shows for a monitor, taken from its \\.\DISPLAYn device name.</summary>
    private static int DisplayNumber(Screen screen)
    {
        Match match = DisplayNumberRegex().Match(screen.DeviceName);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    [GeneratedRegex(@"DISPLAY(\d+)")]
    private static partial Regex DisplayNumberRegex();
}
