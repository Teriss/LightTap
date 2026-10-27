namespace KeyKing;

internal sealed class TimingEditor : UserControl
{
    private readonly DarkNumber baseline = new(0, 0, 3600000, "");
    private readonly DarkNumber amount = new(0, 0, 3600000, "");
    private readonly DarkSelect unit = new();
    private readonly ToggleSwitch random = new() { Text = "波动 ±" };
    private readonly Label range = new() { ForeColor = Theme.Muted, Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly TableLayoutPanel grid;
    private readonly int minimumMs, maximumMs;
    private bool updating;
    internal event EventHandler? ValueChanged;
    internal int Milliseconds => baseline.Value * Factor;
    internal TimeVariation Variation => new(random.Checked, amount.Value * Factor,
        unit.SelectedIndex == 1 ? TimeUnit.Seconds : TimeUnit.Milliseconds);
    private int Factor => unit.SelectedIndex == 1 ? 1000 : 1;

    internal TimingEditor(string title, int initialMs, int minimumMs, int maximumMs, TimeUnit preferredUnit)
    {
        SuspendLayout();
        this.minimumMs = minimumMs;
        this.maximumMs = maximumMs;
        AccessibleName = title;
        BackColor = Theme.Surface;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
        grid.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 18, 32, 4, 28, 16 }) grid.RowStyles.Add(new(SizeType.Absolute, height));
        Controls.Add(grid);
        grid.Controls.Add(new Label { Text = title, Font = new("Microsoft YaHei UI", 8.5f),
            ForeColor = Theme.Muted, Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        var baseRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        baseRow.ColumnStyles.Add(new(SizeType.Percent, 100));
        baseRow.ColumnStyles.Add(new(SizeType.Absolute, 6));
        baseRow.ColumnStyles.Add(new(SizeType.Absolute, 76));
        baseRow.RowStyles.Add(new(SizeType.Percent, 100));
        baseline.Dock = unit.Dock = DockStyle.Fill;
        baseline.Margin = unit.Margin = Padding.Empty;
        baseline.AccessibleName = title + "基准值";
        unit.AccessibleName = title + "单位";
        unit.Items.AddRange(["ms", "s"]);
        unit.SelectedIndex = 0;
        baseRow.Controls.Add(baseline, 0, 0);
        baseRow.Controls.Add(unit, 2, 0);
        grid.Controls.Add(baseRow, 0, 1);
        var randomRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        randomRow.ColumnStyles.Add(new(SizeType.Absolute, 94));
        randomRow.ColumnStyles.Add(new(SizeType.Absolute, 6));
        randomRow.ColumnStyles.Add(new(SizeType.Percent, 100));
        randomRow.RowStyles.Add(new(SizeType.Percent, 100));
        random.Dock = amount.Dock = DockStyle.Fill;
        random.Margin = amount.Margin = Padding.Empty;
        random.Font = range.Font = new("Microsoft YaHei UI", 8);
        random.AccessibleName = title + "启用随机波动";
        amount.AccessibleName = title + "波动范围，与基准值同单位";
        randomRow.Controls.Add(random, 0, 0);
        randomRow.Controls.Add(amount, 2, 0);
        grid.Controls.Add(randomRow, 0, 3);
        grid.Controls.Add(range, 0, 4);
        baseline.ValueChanged += (_, _) => Changed();
        amount.ValueChanged += (_, _) => Changed();
        random.CheckedChanged += (_, _) => { amount.Enabled = random.Checked; Changed(); };
        unit.SelectedIndexChanged += (_, _) =>
        {
            if (updating) return;
            updating = true;
            baseline.SetMaximum(maximumMs / Factor);
            amount.SetMaximum(maximumMs / Factor);
            updating = false;
            Changed();
        };
        LoadValue(initialMs, null, preferredUnit);
        ResumeLayout(true);
    }
    internal void LoadValue(int milliseconds, TimeVariation? variation, TimeUnit fallback)
    {
        updating = true;
        var selectedUnit = variation?.Unit ?? fallback;
        if (!Enum.IsDefined(selectedUnit) || selectedUnit == TimeUnit.Seconds
            && (milliseconds % 1000 != 0 || (variation?.AmountMs ?? 0) % 1000 != 0)) selectedUnit = TimeUnit.Milliseconds;
        unit.SelectedIndex = selectedUnit == TimeUnit.Seconds ? 1 : 0;
        baseline.SetMaximum(maximumMs / Factor);
        amount.SetMaximum(maximumMs / Factor);
        baseline.Value = Math.Clamp(milliseconds, 0, maximumMs) / Factor;
        amount.Value = Math.Clamp(variation?.AmountMs ?? 0, 0, maximumMs) / Factor;
        random.Checked = variation?.Enabled ?? false;
        amount.Enabled = random.Checked;
        updating = false;
        Changed();
    }
    private void Changed()
    {
        if (updating) return;
        UpdateRange(false);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
    internal void UpdateRange(bool conflict)
    {
        var bounds = TimingPolicy.Range(Milliseconds, Variation);
        bool invalid = bounds.Minimum < minimumMs || bounds.Maximum > maximumMs;
        string suffix = Factor == 1000 ? "s" : "ms";
        range.Text = random.Checked
            ? $"{bounds.Minimum / (double)Factor:0.###}–{bounds.Maximum / (double)Factor:0.###} {suffix}"
            : $"固定 {baseline.Value} {suffix}";
        range.ForeColor = invalid || conflict ? Theme.Red : Theme.Muted;
        bool showRange = random.Checked || invalid || conflict;
        range.Visible = showRange;
        if (grid.RowStyles[4].Height != (showRange ? 16 : 0))
        {
            grid.RowStyles[4].Height = showRange ? 16 : 0;
            grid.PerformLayout();
        }
    }
    internal void RefreshEditors()
    {
        baseline.RefreshEditorLayout();
        amount.RefreshEditorLayout();
    }
}
