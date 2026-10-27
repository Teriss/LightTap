using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace KeyKing;

internal enum RecordedInputKind
{
    KeyDown,
    KeyUp,
    MouseMove,
    MouseButtonDown,
    MouseButtonUp,
    MouseWheel
}

internal enum RecordedMouseButton { Left, Right, Middle, X1, X2 }

internal sealed record RecordedInput(long TimeMs, RecordedInputKind Kind, int Code = 0,
    int ScanCode = 0, bool Extended = false, int X = 0, int Y = 0, int Delta = 0);

internal sealed record RecordingData(int Version, string Id, string Name, DateTime CreatedAtUtc,
    long DurationMs, Point StartCursor, Point EndCursor, List<RecordedInput> Events);

internal sealed record PlaybackSettings(int QueueIntervalMs = 0, int LoopCount = 1,
    int LoopIntervalMs = 0, bool PathPerturbation = false, int PathPerturbationPixels = 5);

internal static class AppStorage
{
    internal static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "LightTapData");
    internal static string ConfigPath => Path.Combine(DataDirectory, "LightTap.settings.json");

    internal static void Prepare(string? applicationDirectory = null)
    {
        try
        {
            string baseDirectory = applicationDirectory ?? AppContext.BaseDirectory;
            Directory.CreateDirectory(Path.Combine(baseDirectory, "LightTapData"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal static class RecordingLibrary
{
    internal const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static string LibraryPath => Path.Combine(AppStorage.DataDirectory, "recordings.json");

    internal static List<RecordingData> Load()
    {
        try
        {
            if (!File.Exists(LibraryPath)) return [];
            var stored = JsonSerializer.Deserialize<List<RecordingData>>(File.ReadAllText(LibraryPath)) ?? [];
            return stored.Where(IsValid).ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    internal static void Save(IReadOnlyCollection<RecordingData> recordings)
    {
        Directory.CreateDirectory(AppStorage.DataDirectory);
        WriteAtomic(LibraryPath, JsonSerializer.Serialize(recordings, JsonOptions));
    }

    internal static void Export(RecordingData recording, string path)
    {
        if (!IsValid(recording)) throw new InvalidDataException("录制数据无效，无法导出。");
        WriteAtomic(path, JsonSerializer.Serialize(recording, JsonOptions));
    }

    internal static RecordingData Import(string path)
    {
        RecordingData? recording = JsonSerializer.Deserialize<RecordingData>(File.ReadAllText(path));
        if (recording is null || !IsValid(recording))
            throw new InvalidDataException("录制文件格式无效或版本不受支持。");
        return recording with { Id = Guid.NewGuid().ToString("N") };
    }

    internal static bool IsValid(RecordingData? recording)
    {
        if (recording is null || recording.Version != CurrentVersion || string.IsNullOrWhiteSpace(recording.Id)
            || string.IsNullOrWhiteSpace(recording.Name) || recording.Name.Length > 80
            || recording.DurationMs < 0 || recording.DurationMs > 86_400_000 || recording.Events is null
            || recording.Events.Count > 2_000_000) return false;
        long previous = -1;
        foreach (var item in recording.Events)
        {
            if (item is null || !Enum.IsDefined(item.Kind) || item.TimeMs < previous || item.TimeMs < 0
                || item.TimeMs > recording.DurationMs) return false;
            if (item.Kind is RecordedInputKind.KeyDown or RecordedInputKind.KeyUp
                && (item.Code is < 1 or > 255 || item.ScanCode is < 0 or > 65535)) return false;
            if (item.Kind is RecordedInputKind.MouseButtonDown or RecordedInputKind.MouseButtonUp
                && !Enum.IsDefined((RecordedMouseButton)item.Code)) return false;
            if (item.Kind == RecordedInputKind.MouseWheel && item.Delta is < short.MinValue or > short.MaxValue) return false;
            previous = item.TimeMs;
        }
        return true;
    }

    internal static string UniqueName(string requested, IEnumerable<RecordingData> existing)
    {
        string name = requested.Trim();
        if (name.Length == 0) name = $"录制 {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
        name = name[..Math.Min(name.Length, 80)];
        var names = existing.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(name)) return name;
        for (int number = 2; number < 10000; number++)
        {
            string suffix = $" ({number})";
            string candidate = name[..Math.Min(name.Length, 80 - suffix.Length)] + suffix;
            if (!names.Contains(candidate)) return candidate;
        }
        return $"录制 {Guid.NewGuid():N}";
    }

    private static void WriteAtomic(string path, string text)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null) Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, true);
    }
}

internal sealed class WindowsInputRecorder : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private readonly nint ownerWindow;
    private readonly HotKeyBinding recordHotKey;
    private readonly HotKeyBinding playbackHotKey;
    private readonly List<RecordedInput> events = [];
    private readonly List<RecordedInput> pendingShortcutModifiers = [];
    private readonly HashSet<int> ignoredInitialKeyUps = [];
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Point startCursor;
    private readonly Native.KeyboardHookCallback keyboardCallback;
    private readonly Native.KeyboardHookCallback mouseCallback;
    private nint keyboardHook, mouseHook;
    private bool disposed;

    internal WindowsInputRecorder(nint ownerWindow, HotKeyBinding recordHotKey, HotKeyBinding playbackHotKey)
    {
        this.ownerWindow = ownerWindow;
        this.recordHotKey = recordHotKey;
        this.playbackHotKey = playbackHotKey;
        startCursor = Native.GetCursorPosition();
        keyboardCallback = KeyboardHook;
        mouseCallback = MouseHook;
        keyboardHook = Native.SetWindowsHookEx(WhKeyboardLl, keyboardCallback, Native.GetModuleHandle(null), 0);
        if (keyboardHook == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法安装全局键盘录制钩子。");
        mouseHook = Native.SetWindowsHookEx(WhMouseLl, mouseCallback, Native.GetModuleHandle(null), 0);
        if (mouseHook == 0)
        {
            int error = Marshal.GetLastWin32Error();
            Native.UnhookWindowsHookEx(keyboardHook);
            keyboardHook = 0;
            throw new Win32Exception(error, "无法安装全局鼠标录制钩子。");
        }
        IgnoreIfAlreadyDown((int)recordHotKey.Key);
        IgnoreIfAlreadyDown((int)playbackHotKey.Key);
        foreach (int key in new[] { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
            IgnoreIfAlreadyDown(key);
    }

    internal RecordingData Finish(string name, DateTime createdAtUtc)
    {
        Point endCursor = Native.GetCursorPosition();
        StopHooks();
        lock (events)
        {
            events.AddRange(pendingShortcutModifiers);
            pendingShortcutModifiers.Clear();
            var completed = events.OrderBy(e => e.TimeMs).ToList();
            return new(RecordingLibrary.CurrentVersion, Guid.NewGuid().ToString("N"), name,
                createdAtUtc, Math.Max(clock.ElapsedMilliseconds, completed.LastOrDefault()?.TimeMs ?? 0),
                startCursor, endCursor, completed);
        }
    }

    private nint KeyboardHook(int code, nint message, nint data)
    {
        if (code < 0 || disposed) return Native.CallNextHookEx(keyboardHook, code, message, data);
        if (Native.GetForegroundWindow() == ownerWindow)
        {
            lock (events) pendingShortcutModifiers.Clear();
            return Native.CallNextHookEx(keyboardHook, code, message, data);
        }
        var raw = Marshal.PtrToStructure<Native.KeyboardHookData>(data);
        bool down = message == WmKeyDown || message == WmSysKeyDown;
        bool up = message == WmKeyUp || message == WmSysKeyUp;
        if (!down && !up) return Native.CallNextHookEx(keyboardHook, code, message, data);
        int key = unchecked((int)raw.VirtualKey);
        bool modifier = IsModifier(key);
        long time = clock.ElapsedMilliseconds;
        if (up && ignoredInitialKeyUps.Remove(key)) return Native.CallNextHookEx(keyboardHook, code, message, data);
        var item = new RecordedInput(time, down ? RecordedInputKind.KeyDown : RecordedInputKind.KeyUp,
            key, unchecked((int)raw.ScanCode), (raw.Flags & 1) != 0);

        bool matchesShortcut = down && IsHotKey(key);
        lock (events)
        {
            if (matchesShortcut)
            {
                pendingShortcutModifiers.Clear();
                return Native.CallNextHookEx(keyboardHook, code, message, data);
            }
            if (modifier && IsShortcutModifier(key)) pendingShortcutModifiers.Add(item);
            else
            {
                events.AddRange(pendingShortcutModifiers);
                pendingShortcutModifiers.Clear();
                events.Add(item);
            }
        }
        return Native.CallNextHookEx(keyboardHook, code, message, data);
    }

    private nint MouseHook(int code, nint message, nint data)
    {
        if (code < 0 || disposed) return Native.CallNextHookEx(mouseHook, code, message, data);
        if (Native.GetForegroundWindow() == ownerWindow) return Native.CallNextHookEx(mouseHook, code, message, data);
        var raw = Marshal.PtrToStructure<Native.MouseHookData>(data);
        RecordedInput? item = message.ToInt32() switch
        {
            0x0200 => new(clock.ElapsedMilliseconds, RecordedInputKind.MouseMove, X: raw.Point.X, Y: raw.Point.Y),
            0x0201 => Button(RecordedMouseButton.Left, true),
            0x0202 => Button(RecordedMouseButton.Left, false),
            0x0204 => Button(RecordedMouseButton.Right, true),
            0x0205 => Button(RecordedMouseButton.Right, false),
            0x0207 => Button(RecordedMouseButton.Middle, true),
            0x0208 => Button(RecordedMouseButton.Middle, false),
            0x020B => Button((raw.MouseData >> 16) == 1 ? RecordedMouseButton.X1 : RecordedMouseButton.X2, true),
            0x020C => Button((raw.MouseData >> 16) == 1 ? RecordedMouseButton.X1 : RecordedMouseButton.X2, false),
            0x020A => new(clock.ElapsedMilliseconds, RecordedInputKind.MouseWheel,
                Delta: unchecked((short)(raw.MouseData >> 16))),
            _ => null
        };
        if (item is not null)
        {
            lock (events)
            {
                events.AddRange(pendingShortcutModifiers);
                pendingShortcutModifiers.Clear();
                if (item.Kind != RecordedInputKind.MouseMove || events.Count == 0
                    || events[^1].Kind != RecordedInputKind.MouseMove
                    || events[^1].X != item.X || events[^1].Y != item.Y)
                    events.Add(item);
            }
        }
        return Native.CallNextHookEx(mouseHook, code, message, data);

        RecordedInput Button(RecordedMouseButton button, bool down) =>
            new(clock.ElapsedMilliseconds, down ? RecordedInputKind.MouseButtonDown : RecordedInputKind.MouseButtonUp,
                (int)button);
    }

    private bool HasModifiers(uint modifiers)
    {
        bool down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
        return ((modifiers & 2) == 0 || down(0x11) || down(0xA2) || down(0xA3))
            && ((modifiers & 1) == 0 || down(0x12) || down(0xA4) || down(0xA5))
            && ((modifiers & 4) == 0 || down(0x10) || down(0xA0) || down(0xA1));
    }
    private bool IsHotKey(int key) =>
        key == (int)recordHotKey.Key && HasModifiers(recordHotKey.Modifiers)
        || key == (int)playbackHotKey.Key && HasModifiers(playbackHotKey.Modifiers);
    private bool IsShortcutModifier(int key) =>
        IsShortcutModifier(recordHotKey.Modifiers, key) || IsShortcutModifier(playbackHotKey.Modifiers, key);
    private static bool IsShortcutModifier(uint modifiers, int key) =>
        (modifiers & 2) != 0 && (key is 0x11 or 0xA2 or 0xA3)
        || (modifiers & 1) != 0 && (key is 0x12 or 0xA4 or 0xA5)
        || (modifiers & 4) != 0 && (key is 0x10 or 0xA0 or 0xA1);
    private static bool IsModifier(int key) => key is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;
    private void IgnoreIfAlreadyDown(int key)
    {
        if ((Native.GetAsyncKeyState(key) & 0x8000) != 0) ignoredInitialKeyUps.Add(key);
    }

    private void StopHooks()
    {
        disposed = true;
        if (keyboardHook != 0) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = 0; }
        if (mouseHook != 0) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = 0; }
    }
    public void Dispose() => StopHooks();
}

internal interface IRecordedInputSink
{
    Point CursorPosition { get; }
    Rectangle VirtualScreen { get; }
    void MoveCursor(Point point);
    void Send(RecordedInput input);
}

internal sealed class WindowsRecordedInputSink : IRecordedInputSink
{
    public Point CursorPosition => Native.GetCursorPosition();
    public Rectangle VirtualScreen => SystemInformation.VirtualScreen;
    public void MoveCursor(Point point) => Native.MoveCursor(point);
    public void Send(RecordedInput input) => Native.SendRecorded(input);
}

internal sealed record PlaybackResult(int CompletedLoops, string Reason);

internal static class RecordingPlayback
{
    private const int MoveStepMs = 16;
    internal static async Task<PlaybackResult> PlayAsync(IReadOnlyList<RecordingData> queue,
        PlaybackSettings settings, IProgress<string> status, CancellationToken token,
        IRecordedInputSink? sink = null)
    {
        sink ??= new WindowsRecordedInputSink();
        if (queue.Count == 0) return new(0, "请先将录制数据加入队列。");
        if (settings.QueueIntervalMs is < 0 or > 3_600_000 || settings.LoopIntervalMs is < 0 or > 3_600_000
            || settings.LoopCount is < 0 or > 1_000_000 || settings.PathPerturbationPixels is < 0 or > 20)
            throw new ArgumentOutOfRangeException(nameof(settings), "队列播放设置超出范围。");
        if (queue.Any(x => !RecordingLibrary.IsValid(x))) throw new InvalidDataException("队列中包含无效录制数据。");

        var pressed = new List<RecordedInput>();
        int loops = 0;
        try
        {
            var randomized = queue.Select(x => settings.PathPerturbation
                ? ApplyPathPerturbation(x, settings.PathPerturbationPixels) : x).ToArray();
            await MoveToStartAsync(randomized[0], sink, settings, token);
            while (settings.LoopCount == 0 || loops < settings.LoopCount)
            {
                for (int i = 0; i < randomized.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    status.Report($"正在播放第 {loops + 1} 轮 · 第 {i + 1}/{randomized.Length} 段");
                    await PlayOneAsync(randomized[i], sink, pressed, token);
                    if (i + 1 < randomized.Length)
                        await TransitionAsync(randomized[i + 1], settings.QueueIntervalMs, sink, settings, token);
                }
                loops++;
                status.Report($"已完成 {loops:N0} 轮");
                if (settings.LoopCount > 0 && loops >= settings.LoopCount) break;
                await TransitionAsync(randomized[0], settings.LoopIntervalMs, sink, settings, token);
            }
            return new(loops, settings.LoopCount == 0 ? "已停止循环播放。" : "已完成队列播放。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(loops, "队列播放已停止。");
        }
        finally
        {
            foreach (var input in pressed.AsEnumerable().Reverse())
            {
                try { sink.Send(Release(input)); } catch { }
            }
        }
    }

    internal static RecordingData ApplyPathPerturbation(RecordingData recording, int maxPixels,
        Random? random = null)
    {
        if (maxPixels <= 0 || recording.Events.Count == 0) return recording;
        random ??= Random.Shared;
        var result = new List<RecordedInput>(recording.Events.Count + 4);
        for (int start = 0; start < recording.Events.Count;)
        {
            if (recording.Events[start].Kind != RecordedInputKind.MouseMove)
            { result.Add(recording.Events[start++]); continue; }
            int end = start + 1;
            while (end < recording.Events.Count && recording.Events[end].Kind == RecordedInputKind.MouseMove) end++;
            int count = end - start;
            Point previousOffset = Point.Empty;
            result.Add(recording.Events[start]);
            if (count == 2)
            {
                var first = recording.Events[start];
                var last = recording.Events[start + 1];
                double dx = last.X - first.X, dy = last.Y - first.Y;
                double length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                int offset = RandomSignedOffset(random, maxPixels);
                int middleX = (int)Math.Round((first.X + last.X) / 2d - dy / length * offset);
                int middleY = (int)Math.Round((first.Y + last.Y) / 2d + dx / length * offset);
                result.Add(new((first.TimeMs + last.TimeMs) / 2, RecordedInputKind.MouseMove,
                    X: middleX, Y: middleY));
                result.Add(last);
            }
            else
            {
                for (int i = start + 1; i < end; i++)
                {
                    var item = recording.Events[i];
                    if (i == end - 1) { result.Add(item); continue; }
                    double fade = Math.Min(1d, Math.Min((i - start) / (double)Math.Max(1, count * .15),
                        (end - 1 - i) / (double)Math.Max(1, count * .15)));
                    double angle = random.NextDouble() * Math.PI * 2;
                    double radius = 1 + random.NextDouble() * (maxPixels - 1);
                    var target = new Point((int)Math.Round(Math.Cos(angle) * radius),
                        (int)Math.Round(Math.Sin(angle) * radius));
                    previousOffset = new((int)Math.Round(previousOffset.X * .55 + target.X * .45),
                        (int)Math.Round(previousOffset.Y * .55 + target.Y * .45));
                    var offset = ClampOffset((int)Math.Round(previousOffset.X * fade),
                        (int)Math.Round(previousOffset.Y * fade), maxPixels);
                    result.Add(item with { X = item.X + offset.X, Y = item.Y + offset.Y });
                }
            }
            start = end;
        }
        if (recording.Events.Count(x => x.Kind == RecordedInputKind.MouseMove) == 1)
        {
            int moveIndex = result.FindIndex(x => x.Kind == RecordedInputKind.MouseMove);
            var move = result[moveIndex];
            double dx = move.X - recording.StartCursor.X, dy = move.Y - recording.StartCursor.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length > 0)
            {
                int offset = RandomSignedOffset(random, maxPixels);
                int x = (int)Math.Round((recording.StartCursor.X + move.X) / 2d - dy / length * offset);
                int y = (int)Math.Round((recording.StartCursor.Y + move.Y) / 2d + dx / length * offset);
                long previousTime = moveIndex > 0 ? result[moveIndex - 1].TimeMs : 0;
                long middleTime = previousTime + (move.TimeMs - previousTime) / 2;
                result.Insert(moveIndex, new(middleTime, RecordedInputKind.MouseMove, X: x, Y: y));
            }
        }
        return recording with { Events = result };
    }

    private static Point ClampOffset(int x, int y, int radius)
    {
        while ((long)x * x + (long)y * y > (long)radius * radius)
        {
            if (Math.Abs(x) >= Math.Abs(y)) x -= Math.Sign(x);
            else y -= Math.Sign(y);
        }
        return new(x, y);
    }

    private static int RandomSignedOffset(Random random, int maximum) =>
        random.Next(1, maximum + 1) * (random.Next(2) == 0 ? -1 : 1);

    internal static IReadOnlyList<Point> CreateTransitionPath(Point start, Point end, int jitter,
        Rectangle bounds, Random? random = null)
    {
        if (start == end) return [start];
        random ??= Random.Shared;
        double distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        int steps = Math.Clamp((int)Math.Ceiling(distance / 16), 2, 240);
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double length = Math.Max(1, distance);
        double nx = -dy / length, ny = dx / length;
        double offset1 = jitter > 0 ? random.Next(-jitter, jitter + 1) : 0;
        double offset2 = jitter > 0 ? random.Next(-jitter, jitter + 1) : 0;
        var points = new List<Point>(steps + 1) { start };
        for (int i = 1; i < steps; i++)
        {
            double t = i / (double)steps, u = 1 - t;
            double bend = 3 * u * u * t * offset1 + 3 * u * t * t * offset2;
            double x = u * u * u * start.X + 3 * u * u * t * (start.X + dx / 3)
                + 3 * u * t * t * (start.X + 2 * dx / 3) + t * t * t * end.X + nx * bend;
            double y = u * u * u * start.Y + 3 * u * u * t * (start.Y + dy / 3)
                + 3 * u * t * t * (start.Y + 2 * dy / 3) + t * t * t * end.Y + ny * bend;
            points.Add(new((int)Math.Clamp(Math.Round(x), bounds.Left, bounds.Right - 1),
                (int)Math.Clamp(Math.Round(y), bounds.Top, bounds.Bottom - 1)));
        }
        points.Add(end);
        return points;
    }

    private static async Task PlayOneAsync(RecordingData recording, IRecordedInputSink sink,
        List<RecordedInput> pressed, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        foreach (var item in recording.Events)
        {
            await WaitUntilAsync(clock, item.TimeMs, token);
            token.ThrowIfCancellationRequested();
            if (item.Kind == RecordedInputKind.MouseMove) sink.MoveCursor(new(item.X, item.Y));
            else
            {
                sink.Send(item);
                TrackPressed(item, pressed);
            }
        }
        await WaitUntilAsync(clock, recording.DurationMs, token);
        if (sink.CursorPosition != recording.EndCursor) sink.MoveCursor(recording.EndCursor);
    }

    private static async Task TransitionAsync(RecordingData next, int intervalMs, IRecordedInputSink sink,
        PlaybackSettings settings, CancellationToken token)
    {
        var interval = Stopwatch.StartNew();
        await MoveAlongAsync(sink.CursorPosition, next.StartCursor, sink, settings.PathPerturbation
            ? settings.PathPerturbationPixels : 0, token);
        long remaining = intervalMs - interval.ElapsedMilliseconds;
        if (remaining > 0) await Task.Delay((int)remaining, token);
    }

    private static async Task MoveToStartAsync(RecordingData recording, IRecordedInputSink sink,
        PlaybackSettings settings, CancellationToken token)
    {
        await MoveAlongAsync(sink.CursorPosition, recording.StartCursor, sink,
            settings.PathPerturbation ? settings.PathPerturbationPixels : 0, token);
    }

    private static async Task MoveAlongAsync(Point start, Point end, IRecordedInputSink sink, int jitter,
        CancellationToken token)
    {
        if (start == end) return;
        var path = CreateTransitionPath(start, end, jitter, sink.VirtualScreen);
        double distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        int duration = Math.Max(50, (int)Math.Ceiling(distance / 1200d * 1000));
        var clock = Stopwatch.StartNew();
        for (int i = 1; i < path.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            long target = duration * (long)i / (path.Count - 1);
            await WaitUntilAsync(clock, target, token);
            sink.MoveCursor(path[i]);
        }
        sink.MoveCursor(end);
    }

    private static async Task WaitUntilAsync(Stopwatch clock, long targetMs, CancellationToken token)
    {
        while (clock.ElapsedMilliseconds < targetMs)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay((int)Math.Min(MoveStepMs, Math.Max(1, targetMs - clock.ElapsedMilliseconds)), token);
        }
    }

    private static void TrackPressed(RecordedInput input, List<RecordedInput> pressed)
    {
        if (input.Kind is RecordedInputKind.KeyDown or RecordedInputKind.MouseButtonDown)
        {
            pressed.RemoveAll(x => x.Code == input.Code && x.Kind is RecordedInputKind.KeyDown or RecordedInputKind.MouseButtonDown);
            pressed.Add(input);
        }
        else if (input.Kind is RecordedInputKind.KeyUp or RecordedInputKind.MouseButtonUp)
            pressed.RemoveAll(x => x.Code == input.Code &&
                (x.Kind == RecordedInputKind.KeyDown && input.Kind == RecordedInputKind.KeyUp
                 || x.Kind == RecordedInputKind.MouseButtonDown && input.Kind == RecordedInputKind.MouseButtonUp));
    }

    private static RecordedInput Release(RecordedInput input) => input with
    {
        Kind = input.Kind == RecordedInputKind.KeyDown ? RecordedInputKind.KeyUp : RecordedInputKind.MouseButtonUp
    };
}
