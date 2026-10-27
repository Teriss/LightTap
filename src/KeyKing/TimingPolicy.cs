namespace KeyKing;

internal enum TimeUnit { Milliseconds, Seconds }
internal sealed record TimeVariation(bool Enabled = false, int AmountMs = 0, TimeUnit Unit = TimeUnit.Milliseconds);
internal readonly record struct TimeRange(long Minimum, long Maximum);

internal static class TimingPolicy
{
    internal static TimeRange Range(long baseline, TimeVariation? variation)
    {
        long amount = variation is { Enabled: true } ? variation.AmountMs : 0;
        return new(baseline - amount, baseline + amount);
    }
    internal static long DelayBaseline(ClickOptions options) => options.StartDelayMs ?? (long)options.DelaySeconds * 1000;
    internal static string? Validate(ClickOptions options)
    {
        foreach (var item in new[] {
            (Name: "点击间隔", Base: (long)options.IntervalMs, Variation: options.IntervalVariation, Min: 2, Max: 3600000),
            (Name: "按住时长", Base: (long)options.HoldMs, Variation: options.HoldVariation, Min: 1, Max: 60000),
            (Name: "启动倒计时", Base: DelayBaseline(options), Variation: options.DelayVariation, Min: 0, Max: 30000) })
        {
            if (item.Variation is { AmountMs: < 0 }) return $"{item.Name}的波动范围不能为负数。";
            var range = Range(item.Base, item.Variation);
            if (range.Minimum < item.Min || range.Maximum > item.Max || range.Minimum > range.Maximum)
                return $"{item.Name}的整个取值范围须在 {item.Min:N0}–{item.Max:N0} ms 内，请调整基准值或 ± 波动。";
        }
        var interval = Range(options.IntervalMs, options.IntervalVariation);
        var hold = Range(options.HoldMs, options.HoldVariation);
        if (hold.Maximum >= interval.Minimum)
            return $"间隔最小 {interval.Minimum:N0} ms 必须大于按住最大 {hold.Maximum:N0} ms，请减少按住时长或波动范围。";
        return null;
    }
    internal static int Sample(long baseline, TimeVariation? variation)
    {
        var range = Range(baseline, variation);
        return checked((int)Random.Shared.NextInt64(range.Minimum, range.Maximum + 1));
    }
}
