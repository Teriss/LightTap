using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace KeyKing;

// Opt-in checks send input only to this dedicated foreground test window.
internal sealed class IntegrationChecks : Form
{
    private readonly string reportPath;
    private readonly TextBox text = new() { Dock = DockStyle.Top };
    private readonly Panel mouseTarget = new() { Dock = DockStyle.Fill, BackColor = Color.LightBlue };
    private readonly List<string> passed = [];
    private readonly HookProc keyboardCallback;
    private readonly HookProc mouseCallback;
    private nint keyboardHook, mouseHook;
    private int keyDown, keyUp, injectedKeyboard, mouseDown, mouseUp, injectedMouse;
    private int deliveredMouseDown, deliveredMouseUp;
    private int injectedWheel;
    private readonly List<Point> clickPoints = [];
    private Keys watchedKey = Keys.A;
    private Action? onKeyDown;
    private int hotKeyCount;

    internal IntegrationChecks(string reportPath)
    {
        this.reportPath = Path.GetFullPath(reportPath);
        Text = "LightTap · 专用输入测试窗口";
        ClientSize = new(480, 280);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(mouseTarget);
        Controls.Add(text);
        mouseTarget.MouseDown += (_, e) => { deliveredMouseDown++; clickPoints.Add(mouseTarget.PointToScreen(e.Location)); };
        mouseTarget.MouseUp += (_, _) => deliveredMouseUp++;
        keyboardCallback = KeyboardHook;
        mouseCallback = MouseHook;
        var begin = new Button { Text = "运行输入测试（期间请勿操作鼠标键盘）", Dock = DockStyle.Bottom, Height = 45 };
        Controls.Add(begin);
        begin.Click += async (_, _) => { begin.Enabled = false; await RunChecksAsync(); };
    }

    private async Task RunChecksAsync()
    {
        Point previousCursor = Cursor.Position;
        string? failure = null;
        try
        {
            keyboardHook = SetWindowsHookEx(13, keyboardCallback, GetModuleHandle(null), 0);
            mouseHook = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0);
            Check(keyboardHook != 0 && mouseHook != 0, "低级输入钩子安装");
            await FocusTargetAsync();
            Reset();
            var result = await RunAsync(new(ClickMode.Keyboard, Keys.A, 60, 20, 3, 0, true));
            await Task.Delay(80);
            Check(result.Count == 3 && text.Text == "aaa" && keyDown == 3 && keyUp == 3,
                "键盘：实际接收 3 次字符，按下/释放成对");
            Check(injectedKeyboard == 6, "Windows 可观察到键盘注入标记（明确不提供隐身保证）");

            foreach (var mode in new[] { ClickMode.LeftMouse, ClickMode.RightMouse, ClickMode.MiddleMouse })
            {
                await FocusTargetAsync();
                Cursor.Position = mouseTarget.PointToScreen(new Point(mouseTarget.Width / 2, mouseTarget.Height / 2));
                Reset();
                result = await RunAsync(new(mode, Keys.A, 60, 20, 3, 0, true));
                await Task.Delay(80);
                Check(result.Count == 3 && mouseDown == 3 && mouseUp == 3
                    && deliveredMouseDown == 3 && deliveredMouseUp == 3 && injectedMouse == 6,
                    $"{mode}：实际接收 3 次点击，按下/释放成对且可观察到注入标记");
            }

            await FocusTargetAsync();
            Point center = mouseTarget.PointToScreen(new Point(mouseTarget.Width / 2, mouseTarget.Height / 2));
            Cursor.Position = center;
            Reset();
            result = await RunAsync(new(ClickMode.LeftMouse, Keys.A, 45, 10, 12, 0, true,
                RandomOffset: true, OffsetX: 12, OffsetY: 9));
            await Task.Delay(80);
            Check(result.Count == 12 && deliveredMouseDown == 12 && deliveredMouseUp == 12
                && clickPoints.All(p => Math.Abs(p.X - center.X) <= 12 && Math.Abs(p.Y - center.Y) <= 9)
                && Cursor.Position == center,
                "实际鼠标随机偏移：12 次点击落在当前鼠标周围设定范围内，并回到原位置");

            await FocusTargetAsync();
            Point recordingPoint = mouseTarget.PointToScreen(new Point(mouseTarget.Width / 2, mouseTarget.Height / 2));
            Cursor.Position = recordingPoint;
            Reset();
            var recording = new RecordingData(1, "integration", "集成回放", DateTime.UtcNow, 70,
                recordingPoint, recordingPoint,
            [
                new(0, RecordedInputKind.KeyDown, (int)Keys.A, 30),
                new(10, RecordedInputKind.KeyUp, (int)Keys.A, 30),
                new(20, RecordedInputKind.MouseMove, X: recordingPoint.X, Y: recordingPoint.Y),
                new(30, RecordedInputKind.MouseButtonDown, (int)RecordedMouseButton.Left),
                new(40, RecordedInputKind.MouseButtonUp, (int)RecordedMouseButton.Left),
                new(50, RecordedInputKind.MouseWheel, Delta: 120)
            ]);
            var playback = await RecordingPlayback.PlayAsync([recording], new PlaybackSettings(0, 1),
                new InlineProgress<string>(_ => { }), CancellationToken.None, new WindowsRecordedInputSink());
            await Task.Delay(80);
            Check(playback.CompletedLoops == 1 && text.Text == "a" && keyDown == 1 && keyUp == 1
                && deliveredMouseDown == 1 && deliveredMouseUp == 1 && injectedWheel == 1
                && Cursor.Position == recordingPoint,
                "录制回放集成：目标窗口接收键盘、鼠标按键、滚轮和鼠标位置");

            await FocusTargetAsync();
            Reset(Keys.B);
            using (var cancel = new CancellationTokenSource())
            {
                onKeyDown = cancel.Cancel;
                result = await RunAsync(new(ClickMode.Keyboard, Keys.B, 1100, 1000, 0, 0, true), cancel.Token);
                onKeyDown = null;
                await Task.Delay(60);
                Check(keyDown == 1 && keyUp == 1 && (Native.GetAsyncKeyState((int)Keys.B) & 0x8000) == 0,
                    "按住期间取消：仍然释放按键，无卡键");
            }
            await FocusTargetAsync();
            Reset();
            using (var cancel = new CancellationTokenSource(50))
            {
                var clock = Stopwatch.StartNew();
                result = await RunAsync(new(ClickMode.Keyboard, Keys.A, 100, 20, 0, 3, true), cancel.Token);
                Check(result.Count == 0 && keyDown == 0 && clock.ElapsedMilliseconds < 700,
                    "倒计时取消：无输入且及时停止");
            }
            using (var cancel = new CancellationTokenSource(180))
            {
                var clock = Stopwatch.StartNew();
                result = await RunAsync(new(ClickMode.Keyboard, Keys.A, 60000, 20, 0, 0, true), cancel.Token);
                Check(result.Count == 1 && clock.ElapsedMilliseconds < 800,
                    "长间隔取消：不等待完整点击间隔");
            }

            await FocusTargetAsync();
            Reset(Keys.C);
            using (var other = new Form { Text = "LightTap · 窗口切换测试", Size = new(250, 150) })
            {
                other.Show();
                await FocusTargetAsync();
                onKeyDown = () => { other.Activate(); Native.SetForegroundWindow(other.Handle); };
                result = await RunAsync(new(ClickMode.Keyboard, Keys.C, 150, 100, 0, 0, true));
                onKeyDown = null;
                await Task.Delay(60);
                Check(result.Reason.Contains("窗口发生切换") && keyDown == 1 && keyUp == 1,
                    "前台窗口切换：停止后不再按下，已按下的键得到释放");
            }
            await FocusTargetAsync();
            result = await Task.Run(() => ClickRunner.RunAsync(
                new(ClickMode.Keyboard, Keys.A, 60, 20, 1, 0, true), HandleValue,
                new InlineProgress<string>(_ => { }), new InlineProgress<int>(_ => { }), CancellationToken.None));
            Check(result.Count == 0 && result.Reason.Contains("未开始"), "拒绝向连点器自身开始输入");

            int scheme = -1;
            var errors = new List<string>();
            for (int i = 0; i < GlobalHotKeys.Names.Length; i++)
            {
                if (GlobalHotKeys.RegisterSingle(Handle, GlobalHotKeys.Preset(i), 80, out string message)) { scheme = i; break; }
                errors.Add(message);
            }
            try
            {
                Check(scheme >= 0, $"全局切换热键注册（候选错误：{string.Join(',', errors)}）");
                if (scheme >= 0)
                {
                    var hotKey = Keys.F8;
                    Keys[] modifiers = scheme switch { 1 => [Keys.ControlKey, Keys.Menu],
                        2 => [Keys.ControlKey, Keys.ShiftKey], _ => [] };
                    var options = new ClickOptions(ClickMode.Keyboard, hotKey, 100, 20, 1, 0, true);
                    try
                    {
                        foreach (var modifier in modifiers) Native.Send(options with { Key = modifier }, true);
                        Native.Send(options, true);
                        Native.Send(options, false);
                    }
                    finally
                    {
                        foreach (var modifier in modifiers.Reverse()) Native.Send(options with { Key = modifier }, false);
                    }
                    await Task.Delay(60);
                }
                Check(scheme >= 0 && hotKeyCount == 1,
                    scheme >= 0 ? $"全局 {GlobalHotKeys.Names[scheme]} 实际收到 WM_HOTKEY" : "无法注册可用的全局切换键");
            }
            finally
            {
                Native.UnregisterHotKey(Handle, 80);
            }
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            if (keyboardHook != 0) UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != 0) UnhookWindowsHookEx(mouseHook);
            Cursor.Position = previousCursor;
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new {
                success = failure == null, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                inputSize = Marshal.SizeOf<Native.Input>(), passed, failure
            }, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = failure == null ? 0 : 1;
            Close();
        }
    }
    private nint HandleValue;
    private async Task FocusTargetAsync()
    {
        Activate();
        Native.SetForegroundWindow(Handle);
        text.Focus();
        await Task.Delay(100);
        if (Native.GetForegroundWindow() != Handle)
            throw new InvalidOperationException("测试窗口未能取得前台焦点，取消输入测试。");
        HandleValue = Handle;
    }
    private Task<RunResult> RunAsync(ClickOptions options, CancellationToken token = default) =>
        Task.Run(() => ClickRunner.RunAsync(options, 0, new InlineProgress<string>(_ => { }),
            new InlineProgress<int>(_ => { }), token));
    private void Reset(Keys key = Keys.A)
    {
        watchedKey = key;
        text.Clear();
        keyDown = keyUp = injectedKeyboard = mouseDown = mouseUp = injectedMouse = 0;
        deliveredMouseDown = deliveredMouseUp = 0;
        injectedWheel = 0;
        clickPoints.Clear();
    }
    private void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        passed.Add(description);
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == Native.HotKeyMessage && message.WParam == 80) hotKeyCount++;
        base.WndProc(ref message);
    }
    private nint KeyboardHook(int code, nint message, nint data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == (int)watchedKey)
        {
            bool injected = (Marshal.ReadInt32(data, 8) & 0x10) != 0;
            if (injected)
            {
                injectedKeyboard++;
                if (message == 0x100) { keyDown++; onKeyDown?.Invoke(); }
                if (message == 0x101) keyUp++;
            }
        }
        return CallNextHookEx(0, code, message, data);
    }
    private nint MouseHook(int code, nint message, nint data)
    {
        if (code >= 0 && (Marshal.ReadInt32(data, 12) & 1) != 0)
        {
            if (message == 0x020A) { injectedMouse++; injectedWheel++; }
            if (message == 0x201 || message == 0x204 || message == 0x207) { mouseDown++; injectedMouse++; }
            if (message == 0x202 || message == 0x205 || message == 0x208) { mouseUp++; injectedMouse++; }
        }
        return CallNextHookEx(0, code, message, data);
    }
    private delegate nint HookProc(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int type, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? module);
}
