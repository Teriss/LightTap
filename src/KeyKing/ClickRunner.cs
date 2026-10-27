using System.Diagnostics;
using System.Text.Json.Serialization;

namespace KeyKing;

internal enum ClickMode { LeftMouse, RightMouse, MiddleMouse, Keyboard }
internal sealed record ClickOptions(ClickMode Mode, Keys Key, int IntervalMs,
    int HoldMs, int Count, int DelaySeconds, bool GuardWindow, int HotKeyScheme = 0,
    bool RandomOffset = false, int OffsetX = 5, int OffsetY = 5,
    TimeVariation? IntervalVariation = null, TimeVariation? HoldVariation = null,
    TimeVariation? DelayVariation = null, int? StartDelayMs = null,
    HotKeyBinding? ToggleHotKey = null, HotKeyBinding? RecordingHotKey = null,
    int QueueIntervalMs = 0, int LoopCount = 1, int LoopIntervalMs = 0,
    bool PathPerturbation = false, int PathPerturbationPixels = 5,
    string[]? QueueIds = null)
{
    [JsonIgnore] internal HotKeyBinding ToggleBinding => ToggleHotKey ?? GlobalHotKeys.Preset(HotKeyScheme);
    [JsonIgnore] internal HotKeyBinding RecordingBinding => RecordingHotKey ?? new(Keys.R, 3u);
}
internal sealed record RunResult(int Count, string Reason);

internal static class ClickRunner
{
    internal static async Task<RunResult> RunAsync(ClickOptions options, nint ownWindow,
        IProgress<string> status, IProgress<int> count, CancellationToken token, IInputPlatform? input = null)
    {
        input ??= new WindowsInputPlatform();
        if (options.Count < 0
            || options.OffsetX is < 0 or > 500 || options.OffsetY is < 0 or > 500)
            throw new ArgumentException("次数或鼠标偏移范围无效。");
        if (TimingPolicy.Validate(options) is string timingError) throw new ArgumentException(timingError);
        if (GlobalHotKeys.Validate(options.ToggleBinding) is string hotkeyError)
            throw new ArgumentException(hotkeyError);
        if (GlobalHotKeys.InputConflict(options) is string inputError) throw new ArgumentException(inputError);
        int completed = 0;
        try
        {
            int delayMs = TimingPolicy.Sample(TimingPolicy.DelayBaseline(options), options.DelayVariation);
            var countdown = Stopwatch.StartNew();
            for (long remaining = delayMs; remaining > 0; remaining = delayMs - countdown.ElapsedMilliseconds)
            {
                status.Report($"{remaining / 1000d:0.###} 秒后开始 · 请切换到目标窗口");
                await Task.Delay((int)Math.Min(1000, remaining), token);
            }
            nint target = input.ForegroundWindow;
            if (target == 0 || target == ownWindow)
                return new(0, $"未开始：请先切换到目标窗口，再按 {options.ToggleBinding}。");
            status.Report($"正在连点 · 再按 {options.ToggleBinding} 停止");
            var clock = Stopwatch.StartNew();
            while (options.Count == 0 || completed < options.Count)
            {
                token.ThrowIfCancellationRequested();
                if (options.GuardWindow && input.ForegroundWindow != target)
                    return new(completed, "已停止：目标窗口发生切换。");
                // Do not release a key or button the user is already holding.
                int key = options.Mode switch
                {
                    ClickMode.Keyboard => (int)options.Key,
                    ClickMode.LeftMouse => 1,
                    ClickMode.RightMouse => 2,
                    _ => 4
                };
                if (input.IsPressed(key))
                    return new(completed, "已停止：待连点的按键正在被按住，请松开后重试。");
                bool pressed = false;
                int sampledInterval = TimingPolicy.Sample(options.IntervalMs, options.IntervalVariation);
                int sampledHold = TimingPolicy.Sample(options.HoldMs, options.HoldVariation);
                long pressedAt = clock.ElapsedMilliseconds;
                Point? center = null, movedTo = null;
                try
                {
                    if (options.RandomOffset && options.Mode != ClickMode.Keyboard)
                    {
                        center = input.CursorPosition;
                        var position = MouseOffset.Clamp(center.Value,
                            Random.Shared.Next(-options.OffsetX, options.OffsetX + 1),
                            Random.Shared.Next(-options.OffsetY, options.OffsetY + 1), input.ScreenBounds(center.Value));
                        if (position != center.Value)
                        {
                            input.MoveCursor(position);
                            movedTo = input.CursorPosition;
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    if (options.GuardWindow && input.ForegroundWindow != target) throw new TargetChangedException();
                    pressedAt = clock.ElapsedMilliseconds;
                    input.Send(options, true);
                    pressed = true;
                    await WaitAsync(clock, clock.ElapsedMilliseconds + sampledHold,
                        target, options.GuardWindow, token, input);
                }
                finally
                {
                    try { if (pressed) input.Send(options, false); }
                    finally
                    {
                        if (center.HasValue && movedTo.HasValue && input.CursorPosition == movedTo.Value)
                            input.MoveCursor(center.Value);
                    }
                }
                completed++;
                count.Report(completed);
                if (options.Count > 0 && completed >= options.Count) break;
                // Never catch up with a burst when the scheduler was delayed.
                long next = Math.Max(pressedAt + sampledInterval, clock.ElapsedMilliseconds + 1);
                await WaitAsync(clock, next, target, options.GuardWindow, token, input);
            }
            return new(completed, "已完成设定次数。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(completed, "已停止。");
        }
        catch (TargetChangedException)
        {
            return new(completed, "已停止：目标窗口发生切换。");
        }
    }

    private static async Task WaitAsync(Stopwatch clock, long until, nint target,
        bool guard, CancellationToken token, IInputPlatform input)
    {
        while (clock.ElapsedMilliseconds < until)
        {
            token.ThrowIfCancellationRequested();
            if (guard && input.ForegroundWindow != target)
                throw new TargetChangedException();
            await Task.Delay((int)Math.Min(15, Math.Max(1, until - clock.ElapsedMilliseconds)), token);
        }
        token.ThrowIfCancellationRequested();
        if (guard && input.ForegroundWindow != target) throw new TargetChangedException();
    }
    private sealed class TargetChangedException : Exception;
}
