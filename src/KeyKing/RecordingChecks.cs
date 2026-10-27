namespace KeyKing;

internal static class RecordingChecks
{
    internal static async Task<IReadOnlyList<string>> RunAsync()
    {
        var passed = new List<string>();
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            passed.Add(description);
        }

        var path = new RecordingData(1, "path", "路径测试", DateTime.UtcNow, 40,
            new(10, 10), new(70, 10),
        [
            new(0, RecordedInputKind.MouseMove, X: 10, Y: 10),
            new(10, RecordedInputKind.MouseMove, X: 30, Y: 10),
            new(20, RecordedInputKind.MouseMove, X: 50, Y: 10),
            new(30, RecordedInputKind.MouseMove, X: 70, Y: 10)
        ]);
        var jittered = RecordingPlayback.ApplyPathPerturbation(path, 5, new Random(17));
        var first = jittered.Events.First(x => x.Kind == RecordedInputKind.MouseMove);
        var last = jittered.Events.Last(x => x.Kind == RecordedInputKind.MouseMove);
        bool bounded = jittered.Events.Where(x => x.Kind == RecordedInputKind.MouseMove)
            .Zip(path.Events, (actual, original) => Math.Sqrt(Math.Pow(actual.X - original.X, 2)
                + Math.Pow(actual.Y - original.Y, 2))).All(distance => distance <= 5.01);
        Check(first.X == 10 && first.Y == 10 && last.X == 70 && last.Y == 10 && bounded
            && jittered.Events.Where(x => x.Kind == RecordedInputKind.MouseMove)
                .Zip(path.Events, (actual, original) => actual.X != original.X || actual.Y != original.Y).Any(),
            "录制路径扰动限制在 ±5px 内，并保持首尾坐标不变");
        Check(RecordingLibrary.IsValid(jittered), "路径扰动后录制的事件时序仍然有效");
        var singleMove = new RecordingData(1, "single-move", "单点移动", DateTime.UtcNow, 20,
            new(0, 0), new(20, 0), [new(20, RecordedInputKind.MouseMove, X: 20, Y: 0)]);
        var singleJitter = RecordingPlayback.ApplyPathPerturbation(singleMove, 5, new Random(3));
        Check(singleJitter.Events.Count == 2 && singleJitter.Events[^1].X == 20
            && singleJitter.Events[^1].Y == 0 && singleJitter.Events[0].Y != 0,
            "单个鼠标移动事件也生成随机中间路径并保留最终坐标");

        string tempDirectory = Path.Combine(Path.GetTempPath(), "KeyKing-recording-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            string file = Path.Combine(tempDirectory, "path.kkrec");
            RecordingLibrary.Export(path, file);
            var imported = RecordingLibrary.Import(file);
            Check(imported.Id != path.Id && imported.Name == path.Name && imported.StartCursor == path.StartCursor
                && imported.EndCursor == path.EndCursor && RecordingLibrary.IsValid(imported),
                ".kkrec 导入导出保留事件与鼠标起终点，并生成独立录制标识");

            string renamedDataRoot = Path.Combine(tempDirectory, "renamed-data");
            Directory.CreateDirectory(renamedDataRoot);
            string oldConfig = Path.Combine(renamedDataRoot, "KeyKing.settings.json");
            File.WriteAllText(oldConfig, "old-settings");
            AppStorage.Prepare(renamedDataRoot);
            Check(File.Exists(oldConfig) && Directory.Exists(Path.Combine(renamedDataRoot, "LightTapData")),
                "新应用使用 LightTapData 目录，保留但不迁移旧版配置");
        }
        finally { Directory.Delete(tempDirectory, true); }

        var firstClip = new RecordingData(1, "first", "第一段", DateTime.UtcNow, 5,
            new(100, 100), new(100, 100),
        [
            new(0, RecordedInputKind.KeyDown, 65, 30),
            new(1, RecordedInputKind.MouseMove, X: 100, Y: 100),
            new(2, RecordedInputKind.MouseWheel, Delta: 120),
            new(4, RecordedInputKind.KeyUp, 65, 30)
        ]);
        var secondClip = new RecordingData(1, "second", "第二段", DateTime.UtcNow, 3,
            new(105, 100), new(105, 100),
        [
            new(0, RecordedInputKind.MouseMove, X: 105, Y: 100),
            new(1, RecordedInputKind.MouseButtonDown, (int)RecordedMouseButton.Left),
            new(2, RecordedInputKind.MouseButtonUp, (int)RecordedMouseButton.Left)
        ]);
        var sink = new FakeSink { Position = new(100, 100) };
        var result = await RecordingPlayback.PlayAsync([firstClip, secondClip],
            new PlaybackSettings(0, 2, 0, false, 5), new InlineProgress<string>(_ => { }),
            CancellationToken.None, sink);
        var kinds = sink.Sent.Select(x => x.Kind).ToArray();
        Check(result.CompletedLoops == 2 && result.Reason.Contains("完成")
            && kinds.SequenceEqual([
                RecordedInputKind.KeyDown, RecordedInputKind.MouseWheel, RecordedInputKind.KeyUp,
                RecordedInputKind.MouseButtonDown, RecordedInputKind.MouseButtonUp,
                RecordedInputKind.KeyDown, RecordedInputKind.MouseWheel, RecordedInputKind.KeyUp,
                RecordedInputKind.MouseButtonDown, RecordedInputKind.MouseButtonUp])
            && sink.Moves.Any(x => x == new Point(105, 100)) && sink.Position == new Point(105, 100),
            "队列按顺序回放键鼠和滚轮事件，完成两轮并执行片段及循环转场");

        using var cancellation = new CancellationTokenSource();
        var cancelSink = new FakeSink { Position = new(20, 20) };
        cancelSink.OnSend = input =>
        {
            if (input.Kind == RecordedInputKind.KeyDown) cancellation.Cancel();
        };
        var held = new RecordingData(1, "held", "按键释放", DateTime.UtcNow, 1000,
            new(20, 20), new(20, 20),
            [new(0, RecordedInputKind.KeyDown, 66, 48)]);
        var stopped = await RecordingPlayback.PlayAsync([held], new PlaybackSettings(0, 0),
            new InlineProgress<string>(_ => { }), cancellation.Token, cancelSink);
        Check(stopped.Reason.Contains("停止") && cancelSink.Sent.Select(x => x.Kind)
            .SequenceEqual([RecordedInputKind.KeyDown, RecordedInputKind.KeyUp]),
            "停止播放时释放仍处于按下状态的键");

        var transition = RecordingPlayback.CreateTransitionPath(new(-50, 20), new(150, 80), 5,
            new(-100, 0, 400, 200), new Random(9));
        Check(transition.First() == new Point(-50, 20) && transition.Last() == new Point(150, 80)
            && transition.All(point => new Rectangle(-100, 0, 400, 200).Contains(point)),
            "跨录制转场路径保持精确起终点并处于虚拟屏幕范围");

        return passed;
    }

    private sealed class FakeSink : IRecordedInputSink
    {
        internal Point Position = Point.Empty;
        internal readonly List<Point> Moves = [];
        internal readonly List<RecordedInput> Sent = [];
        internal Action<RecordedInput>? OnSend;
        public Point CursorPosition => Position;
        public Rectangle VirtualScreen => new(-100, 0, 400, 200);
        public void MoveCursor(Point point) { Position = point; Moves.Add(point); }
        public void Send(RecordedInput input) { Sent.Add(input); OnSend?.Invoke(input); }
    }
}
