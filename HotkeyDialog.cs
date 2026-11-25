namespace ScreenCheat;

/// <summary>Captures a new hotkey: the user simply presses the combination they want.</summary>
internal sealed class HotkeyDialog : Form
{
    private readonly Label _display;

    public HotkeyDialog(KeyCombo current)
    {
        Result = current;

        Text = "修改热键";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(380, 168);

        var hint = new Label
        {
            Text = "直接按下新的组合键。需要带 Ctrl / Alt / Shift，或者单独用 F1–F24。",
            Location = new Point(16, 14),
            Size = new Size(348, 36),
        };
        _display = new Label
        {
            Text = current.ToString(),
            Location = new Point(16, 56),
            Size = new Size(348, 44),
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
        };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(188, 120), Size = new Size(84, 32) };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(280, 120), Size = new Size(84, 32) };
        Controls.AddRange([hint, _display, ok, cancel]);
    }

    public KeyCombo Result { get; private set; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }
        if (keyData == Keys.Enter)
        {
            DialogResult = DialogResult.OK;
            return true;
        }

        KeyCombo combo = KeyCombo.FromKeyData(keyData);
        if (combo.IsUsable)
        {
            Result = combo;
            _display.Text = combo.ToString();
        }
        return true; // every key press belongs to the capture, not to the buttons
    }
}
