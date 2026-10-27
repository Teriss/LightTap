using System.Text.Json;

namespace KeyKing;

internal static class CoreChecks
{
    internal static async Task RunAsync(string output)
    {
        var passed = new List<string>();
        string? failure = null;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            passed.Add(description);
        }
        Task<RunResult> Run(ClickOptions options, FakeInput input, CancellationToken token = default) =>
            ClickRunner.RunAsync(options, 99, new InlineProgress<string>(_ => { }), new InlineProgress<int>(_ => { }), token, input);
        var baseline = new ClickOptions(ClickMode.LeftMouse, Keys.A, 4, 1, 12, 0, true);
        try
        {
            var input = new FakeInput();
            await Run(baseline, input);
            Check(input.Moves.Count == 0 && input.Events.Count == 24, "偏移关闭：保持原有鼠标位置和按下/释放次数");
            input = new();
            await Run(baseline with { RandomOffset = true, OffsetX = 7, OffsetY = 9 }, input);
            Check(input.Events.All(e => Math.Abs(e.Position.X - 100) <= 7 && Math.Abs(e.Position.Y - 100) <= 9)
                && input.CursorPosition == new Point(100, 100) && input.Events.Count == 24,
                "围绕当前鼠标位置随机点击：始终在范围内且逐次回位，无累计漂移");
            input = new();
            await Run(baseline with { RandomOffset = true, OffsetX = 0, OffsetY = 0 }, input);
            Check(input.Moves.Count == 0, "零范围：不移动鼠标");
            input = new();
            await Run(baseline with { RandomOffset = true, Mode = ClickMode.Keyboard }, input);
            Check(input.Moves.Count == 0 && input.CursorReads == 0, "键盘模式：忽略已保存的鼠标偏移开关");
            Check(MouseOffset.Clamp(new(-1919, 1), -500, -500, new(-1920, 0, 1920, 1080)) == new Point(-1920, 0)
                && MouseOffset.Clamp(new(-1, 1079), 500, 500, new(-1920, 0, 1920, 1080)) == new Point(-1, 1079),
                "屏幕边缘与负坐标显示器：不越出显示器边界");
            input = new();
            input.OnDown = () => { if (input.Events.Count == 1) input.Position = new(140, 130); };
            await Run(baseline with { RandomOffset = true, Count = 2 }, input);
            Check(input.Events[1].Position == new Point(140, 130)
                && Math.Abs(input.Events[2].Position.X - 140) <= 5 && Math.Abs(input.Events[2].Position.Y - 130) <= 5
                && input.CursorPosition == new Point(140, 130), "手动移动鼠标：不抢回位置，下一次跟随新中心");
            input = new();
            using (var source = new CancellationTokenSource())
            {
                input.OnDown = source.Cancel;
                var result = await Run(baseline with { RandomOffset = true, HoldMs = 100, IntervalMs = 110 }, input, source.Token);
                Check(result.Count == 0 && input.Events.Count == 2 && !input.Events[1].Down
                    && input.CursorPosition == new Point(100, 100), "偏移点击中取消：先释放按键，再恢复鼠标位置");
            }
            input = new();
            input.OnDown = () => input.Foreground = 2;
            var stopped = await Run(baseline with { RandomOffset = true }, input);
            Check(stopped.Reason.Contains("窗口发生切换") && input.Events.Count == 2
                && input.CursorPosition == new Point(100, 100), "窗口切换：停止并释放、回位");
            input = new() { ThrowOnDown = true };
            bool threw = false;
            try { await Run(baseline with { RandomOffset = true }, input); }
            catch (InvalidOperationException) { threw = true; }
            Check(threw && input.CursorPosition == new Point(100, 100), "输入失败：仍恢复鼠标位置并上报错误");
            var old = JsonSerializer.Deserialize<ClickOptions>("{\"Mode\":0,\"Key\":65,\"IntervalMs\":100,\"HoldMs\":20,\"Count\":0,\"DelaySeconds\":3,\"GuardWindow\":true,\"HotKeyScheme\":1}")!;
            Check(!old.RandomOffset && old.OffsetX == 5 && old.OffsetY == 5 && old.HotKeyScheme == 1,
                "旧配置兼容：偏移默认关闭，范围默认 ±5px，保留原热键");
            var saved = baseline with { RandomOffset = true, OffsetX = 13, OffsetY = 21 };
            Check(JsonSerializer.Deserialize<ClickOptions>(JsonSerializer.Serialize(saved)) == saved,
                "新配置：偏移开关及横纵范围可完整保存、读取");
            passed.AddRange(await RecordingChecks.RunAsync());
        }
        catch (Exception error) { failure = error.ToString(); }
        string path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { success = failure == null, inputInjected = false, passed, failure },
            new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = failure == null ? 0 : 1;
    }
    private sealed class FakeInput : IInputPlatform
    {
        internal nint Foreground = 1;
        internal Point Position = new(100, 100);
        internal int CursorReads;
        internal bool ThrowOnDown;
        internal Action? OnDown;
        internal readonly List<Point> Moves = [];
        internal readonly List<(bool Down, Point Position)> Events = [];
        public nint ForegroundWindow => Foreground;
        public Point CursorPosition { get { CursorReads++; return Position; } }
        public bool IsPressed(int key) => false;
        public Rectangle ScreenBounds(Point point) => new(0, 0, 200, 200);
        public void MoveCursor(Point point) { Position = point; Moves.Add(point); }
        public void Send(ClickOptions options, bool down)
        {
            if (down && ThrowOnDown) throw new InvalidOperationException("模拟输入失败");
            Events.Add((down, Position));
            if (down) OnDown?.Invoke();
        }
    }
}
