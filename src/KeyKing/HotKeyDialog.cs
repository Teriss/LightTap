using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KeyKing;

internal sealed class HotKeyRecorder : ModernButton
{
    private HotKeyBinding binding = new(Keys.F8);
    private bool recording;
    private Keys? swallowRelease;
    private nint hook;
    private Native.KeyboardHookCallback? hookCallback;
    internal string? CaptureError { get; private set; }
    internal event EventHandler? CaptureChanged;
    internal bool Recording => recording;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal HotKeyBinding Binding
    {
        get => binding;
        set { binding = value; Text = value.ToString(); }
    }
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        ReleaseHook();
        swallowRelease = null;
        recording = true;
        Selected = true;
        Text = "请按下快捷键…";
        Focus();
        CaptureError = null;
        hookCallback ??= CaptureHook;
        hook = Native.SetWindowsHookEx(13, hookCallback, Native.GetModuleHandle(null), 0);
        if (hook == 0)
        {
            CaptureError = $"无法录制快捷键（错误 {Marshal.GetLastWin32Error()}），请重试。";
            CancelCapture();
        }
        CaptureChanged?.Invoke(this, EventArgs.Empty);
    }
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (!recording) return base.ProcessCmdKey(ref message, keyData);
        RecordKey(keyData);
        return true;
    }
    protected override bool IsInputKey(Keys keyData) => recording || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!recording) { base.OnKeyDown(e); return; }
        RecordKey(e.KeyData);
        e.Handled = e.SuppressKeyPress = true;
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (recording || e.KeyCode == swallowRelease)
        {
            if (e.KeyCode == swallowRelease) swallowRelease = null;
            e.Handled = e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyUp(e);
    }
    private void RecordKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LControlKey or Keys.RControlKey
            or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin) return;
        uint modifiers = ((keyData & Keys.Control) != 0 ? 2u : 0u)
            | ((keyData & Keys.Alt) != 0 ? 1u : 0u) | ((keyData & Keys.Shift) != 0 ? 4u : 0u);
        CompleteCapture(key, modifiers);
    }
    private void CompleteCapture(Keys key, uint modifiers)
    {
        Binding = new(key, modifiers);
        swallowRelease = key;
        recording = false;
        Selected = false;
        ReleaseHook();
        CaptureChanged?.Invoke(this, EventArgs.Empty);
    }
    private nint CaptureHook(int code, nint message, nint data)
    {
        if (code < 0 || !recording) return Native.CallNextHookEx(0, code, message, data);
        if (FindForm() is not Form owner || Native.GetForegroundWindow() != owner.Handle)
        {
            CancelCapture();
            return Native.CallNextHookEx(0, code, message, data);
        }
        Keys key = (Keys)Marshal.ReadInt32(data);
        bool down = message == 0x100 || message == 0x104;
        if (!down) return Native.CallNextHookEx(0, code, message, data);
        bool modifier = key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or Keys.ShiftKey
            or Keys.LShiftKey or Keys.RShiftKey or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;
        // Forward modifier transitions and key-up events so no physical key can become stuck.
        if (modifier) return Native.CallNextHookEx(0, code, message, data);
        bool Held(Keys code) => (Native.GetAsyncKeyState((int)code) & 0x8000) != 0;
        uint modifiers = (Held(Keys.ControlKey) ? 2u : 0u) | (Held(Keys.Menu) ? 1u : 0u)
            | (Held(Keys.ShiftKey) ? 4u : 0u) | (Held(Keys.LWin) || Held(Keys.RWin) ? 8u : 0u);
        CompleteCapture(key, modifiers);
        // Capture occupied combinations before the OS dispatches another app's WM_HOTKEY.
        return 1;
    }
    private void ReleaseHook()
    {
        if (hook == 0) return;
        Native.UnhookWindowsHookEx(hook);
        hook = 0;
    }
    internal void CancelCapture()
    {
        recording = false;
        Selected = false;
        Text = binding.ToString();
        ReleaseHook();
        if (!Disposing && !IsDisposed) CaptureChanged?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnLostFocus(EventArgs e)
    {
        CancelCapture();
        swallowRelease = null;
        base.OnLostFocus(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { recording = false; ReleaseHook(); }
        base.Dispose(disposing);
    }
}

internal sealed class HotKeyDialog : Form
{
    private readonly HotKeyRecorder toggle = new() { Dock = DockStyle.Fill, AccessibleName = "录制开始停止快捷键" };
    private readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Margin = new(0, 12, 0, 0) };
    private readonly ModernButton apply = new() { Text = "检查并应用", Primary = true, Dock = DockStyle.Fill };
    internal HotKeyBinding Binding => toggle.Binding;
    private readonly Keys? repeatedKey;

    internal HotKeyDialog(HotKeyBinding currentToggle, Keys? repeatedKey, string purpose = "开始 / 停止")
    {
        SuspendLayout();
        this.repeatedKey = repeatedKey;
        Text = "自定义快捷键";
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new("Microsoft YaHei UI", 9.5f);
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(540, 300);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        toggle.Binding = currentToggle;
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 1, RowCount = 6 };
        grid.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 32, 24, 40, 28 }) grid.RowStyles.Add(new(SizeType.Absolute, height));
        grid.RowStyles.Add(new(SizeType.Percent, 100));
        grid.RowStyles.Add(new(SizeType.Absolute, 40));
        Controls.Add(grid);
        Label Label(string text) => new() { Text = text, ForeColor = Theme.Muted, Dock = DockStyle.Fill, Margin = Padding.Empty };
        grid.Controls.Add(new Label { Text = $"设置{purpose}快捷键", Font = new(Font.FontFamily, 12, FontStyle.Bold),
            ForeColor = Theme.Text, Dock = DockStyle.Fill }, 0, 0);
        grid.Controls.Add(Label(purpose + "快捷键"), 0, 1);
        grid.Controls.Add(toggle, 0, 2);
        grid.Controls.Add(Label("点击输入框，再按按键或 Ctrl / Alt / Shift 组合键。"), 0, 3);
        grid.Controls.Add(status, 0, 4);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        actions.ColumnStyles.Add(new(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new(SizeType.Absolute, 90));
        actions.ColumnStyles.Add(new(SizeType.Absolute, 10));
        actions.ColumnStyles.Add(new(SizeType.Absolute, 150));
        actions.RowStyles.Add(new(SizeType.Percent, 100));
        var cancel = new ModernButton { Text = "取消", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(cancel, 1, 0);
        actions.Controls.Add(apply, 3, 0);
        grid.Controls.Add(actions, 0, 5);
        CancelButton = cancel;
        toggle.CaptureChanged += (_, _) => ValidateDraft();
        Deactivate += (_, _) => toggle.CancelCapture();
        apply.Click += (_, _) => Apply();
        ValidateDraft();
        ResumeLayout(true);
    }
    private string? DraftError()
    {
        string? invalid = toggle.CaptureError ?? GlobalHotKeys.Validate(toggle.Binding);
        if (invalid != null) return invalid;
        if (repeatedKey is Keys key && toggle.Binding.Key == key && toggle.Binding.Modifiers == 0)
            return "快捷键不能与当前的键盘连点按键相同，请加修饰键或选择其他键。";
        return null;
    }
    private void ValidateDraft()
    {
        string? error = DraftError();
        bool recording = toggle.Recording;
        apply.Enabled = !recording && error == null;
        status.ForeColor = error == null ? Theme.Muted : Theme.Red;
        status.Text = recording ? "录制中，请按下完整快捷键。" : error ?? "尚未检查占用；点击“检查并应用”注册快捷键。";
    }
    private void Apply()
    {
        if (DraftError() is string invalid) { status.Text = invalid; status.ForeColor = Theme.Red; return; }
        bool available;
        string message;
        try { available = GlobalHotKeys.RegisterSingle(Handle, toggle.Binding, 80, out message); }
        finally { GlobalHotKeys.Unregister(Handle, 80); }
        status.Text = message;
        status.ForeColor = available ? Theme.Green : Theme.Red;
        if (!available) return;
        DialogResult = DialogResult.OK;
        Close();
    }
    protected override void WndProc(ref Message message)
    {
        // A captured Alt combination must not activate the dialog's system menu on release.
        if (message.Msg == 0x0112 && (message.WParam.ToInt64() & 0xfff0) == 0xf100) return;
        base.WndProc(ref message);
    }
}
