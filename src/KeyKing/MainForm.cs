using System.Runtime.InteropServices;
using System.Text.Json;

namespace KeyKing;

internal sealed class MainForm : Form
{
    private readonly DarkSelect key = new() { AccessibleName = "键盘按键", Dock = DockStyle.Fill };
    private readonly ModernButton editHotKeys = new() { Text = "自定义快捷键", Dock = DockStyle.Fill, MaximumSize = new(0, 38) };
    private readonly TimingEditor interval = new("点击间隔", 100, 2, 3600000, TimeUnit.Milliseconds);
    private readonly TimingEditor hold = new("按住时长", 20, 1, 60000, TimeUnit.Milliseconds);
    private readonly DarkNumber limit = new(0, 0, 1000000, "次");
    private readonly TimingEditor delay = new("启动倒计时", 3000, 0, 30000, TimeUnit.Seconds);
    private readonly DarkNumber offsetX = new(5, 0, 500, "px");
    private readonly DarkNumber offsetY = new(5, 0, 500, "px");
    private readonly ToggleSwitch randomOffset = new() { Text = "随机位置偏移", AccessibleName = "启用鼠标随机位置偏移", Dock = DockStyle.Fill };
    private readonly ToggleSwitch guard = new() { Text = "切换窗口时自动停止", Checked = true, Dock = DockStyle.Fill };
    private readonly Label state = Caption("", 8.5f);
    private readonly Label badge = Caption("●  就绪", 9, Theme.Green);
    private readonly Label counter = Caption("0 次", 22, Theme.Text);
    private readonly Label shortcuts = Caption("", 8, Theme.Muted);
    private readonly ModernButton start = new() { Text = "开始连点", Primary = true, Dock = DockStyle.Fill };
    private readonly ModernButton stop = new() { Text = "停止", Danger = true, Dock = DockStyle.Fill, Enabled = false };
    private readonly ModernButton mouseTab = new() { Text = "鼠标连点", Selected = true, Dock = DockStyle.Fill };
    private readonly ModernButton keyboardTab = new() { Text = "键盘连点", Dock = DockStyle.Fill };
    private readonly ModernButton queueTab = new() { Text = "录制队列", Dock = DockStyle.Fill };
    private readonly ModernButton[] mouseButtons = [new() { Text = "左键" }, new() { Text = "右键" }, new() { Text = "中键" }];
    private readonly TableLayoutPanel mousePicker = Grid(3);
    private readonly Label inputTitle = Caption("鼠标按键", 9);
    private readonly DarkListBox recordingList = new() { AccessibleName = "录制库", EmptyText = "暂无录制", Dock = DockStyle.Fill };
    private readonly DarkListBox queueList = new() { AccessibleName = "待执行队列", EmptyText = "队列为空", Dock = DockStyle.Fill };
    private readonly ModernButton recordButton = new() { Text = "开始录制", Primary = true, Dock = DockStyle.Fill };
    private readonly ModernButton renameRecordingButton = new() { Text = "改名", Dock = DockStyle.Fill };
    private readonly ModernButton deleteRecordingButton = new() { Text = "删除", Danger = true, Dock = DockStyle.Fill };
    private readonly ModernButton importRecordingsButton = new() { Text = "导入", Dock = DockStyle.Fill };
    private readonly ModernButton exportRecordingButton = new() { Text = "导出所选", Dock = DockStyle.Fill };
    private readonly ModernButton addToQueueButton = new() { Text = "加入播放队列 →", Primary = true, Dock = DockStyle.Fill };
    private readonly ModernButton editRecordingHotKeyButton = new() { Text = "设置录制快捷键", Dock = DockStyle.Fill };
    private readonly ModernButton queueUpButton = new() { Text = "上移", Dock = DockStyle.Fill };
    private readonly ModernButton queueDownButton = new() { Text = "下移", Dock = DockStyle.Fill };
    private readonly ModernButton queueRemoveButton = new() { Text = "移出队列", Dock = DockStyle.Fill };
    private readonly DarkNumber queueInterval = new(0, 0, 3_600_000, "ms");
    private readonly DarkNumber loopCount = new(1, 0, 1_000_000, "轮");
    private readonly DarkNumber loopInterval = new(0, 0, 3_600_000, "ms");
    private readonly ToggleSwitch pathPerturbation = new() { Text = "扰动", Dock = DockStyle.Fill };
    private readonly DarkNumber pathPerturbationPixels = new(5, 0, 20, "px");
    private readonly List<Control> settings = [];
    private readonly List<Control> queueControls = [];
    private readonly List<RecordingData> recordings = [];
    private readonly List<string> queueIds = [];
    private readonly ToolTip controlHints = new() { AutomaticDelay = 350, AutoPopDelay = 9000, ShowAlways = true };
    private readonly bool preview;
    private Control? clickerPage;
    private Control? recordingPage;
    private Control? pageHost;
    private ClickMode selectedMode;
    private CancellationTokenSource? cancellation;
    private Task? running;
    private bool hotKeysReady, recordingHotKeyReady, closing, editingHotKeys, ready, customHotKeys,
        customRecordingHotKey, queuePageSelected;
    private int clickerCount, queueCount;
    private int legacyScheme;
    private HotKeyBinding toggleBinding = new(Keys.F8);
    private HotKeyBinding recordingBinding = new(Keys.R, 3u);
    private string recordingHotKeyIssue = "";
    private WindowsInputRecorder? activeRecorder;
    private string activeRecordingName = "";
    private DateTime activeRecordingCreatedAt;
    private static string ConfigPath => AppStorage.ConfigPath;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Background);
    }

    internal MainForm(bool preview = false)
    {
        this.preview = preview;
        SuspendLayout();
        Text = "LightTap · 便携连点器";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new("Microsoft YaHei UI", 9.5f);
        ForeColor = Theme.Text;
        BackColor = Theme.Background;
        ClientSize = new(900, 640);
        MinimumSize = new(860, 660);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 46));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 104));
        Controls.Add(root);

        var modes = new TableLayoutPanel { Dock = DockStyle.None, Height = 38, Width = 456,
            ColumnCount = 3, Margin = new(0, 0, 0, 16) };
        modes.ColumnStyles.Add(new(SizeType.Percent, 33.33f));
        modes.ColumnStyles.Add(new(SizeType.Percent, 33.33f));
        modes.ColumnStyles.Add(new(SizeType.Percent, 33.34f));
        modes.RowStyles.Add(new(SizeType.Percent, 100));
        modes.Controls.Add(mouseTab, 0, 0);
        modes.Controls.Add(keyboardTab, 1, 0);
        modes.Controls.Add(queueTab, 2, 0);
        mouseTab.Margin = new(0, 0, 5, 0);
        keyboardTab.Margin = new(5, 0, 0, 0);
        queueTab.Margin = new(5, 0, 0, 0);
        var modeRow = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        modeRow.Controls.Add(modes);
        modes.Location = Point.Empty;
        root.Controls.Add(modeRow, 0, 0);

        var content = new BufferedTableLayoutPanel { Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ColumnCount = 3, Margin = Padding.Empty };
        content.ColumnStyles.Add(new(SizeType.Percent, 54));
        content.ColumnStyles.Add(new(SizeType.Absolute, 16));
        content.ColumnStyles.Add(new(SizeType.Percent, 46));
        content.RowStyles.Add(new(SizeType.Percent, 100));
        clickerPage = content;
        var queuePage = recordingPage = BuildRecordingPage();
        queuePage.Visible = false;
        var host = pageHost = new BufferedPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        queuePage.Dock = DockStyle.None;
        queuePage.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        host.Controls.Add(content);
        host.Controls.Add(queuePage);
        host.SizeChanged += (_, _) => ResizePages();
        root.Controls.Add(host, 0, 1);
        var left = Column(112, 14);
        var right = Column(150, 14);
        content.Controls.Add(left, 0, 0);
        content.Controls.Add(right, 2, 0);

        var input = Card();
        var inputLayout = Stack((SizeType.Absolute, 24), (SizeType.Percent, 100));
        input.Controls.Add(inputLayout);
        inputLayout.Controls.Add(inputTitle, 0, 0);
        var picker = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        for (int i = 0; i < mouseButtons.Length; i++)
        {
            int buttonIndex = i;
            var button = mouseButtons[i];
            button.Dock = DockStyle.Fill;
            button.Margin = new(i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0);
            button.Click += (_, _) => SetMode((ClickMode)buttonIndex);
            mousePicker.Controls.Add(button, i, 0);
            settings.Add(button);
        }
        picker.Controls.AddRange([mousePicker, key]);
        inputLayout.Controls.Add(picker, 0, 1);
        left.Controls.Add(input, 0, 0);
        var keys = Enumerable.Range('A', 26).Select(k => (Keys)k)
            .Concat(Enumerable.Range('0', 10).Select(k => (Keys)k))
            .Concat([Keys.Space, Keys.Enter, Keys.Tab, Keys.Escape, Keys.Back, Keys.Delete,
                Keys.Insert, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Left, Keys.Right, Keys.Up, Keys.Down])
            .Concat(Enumerable.Range((int)Keys.F1, 12).Select(k => (Keys)k));
        foreach (var item in keys) key.Items.Add(new KeyChoice(item));
        key.SelectedIndex = 0;
        settings.AddRange([key, mouseTab, keyboardTab, queueTab]);

        var rhythm = Card();
        rhythm.Padding = new(14);
        var rhythmLayout = Stack((SizeType.Absolute, 24), (SizeType.Percent, 100));
        rhythm.Controls.Add(rhythmLayout);
        rhythmLayout.Controls.Add(Caption("点击节奏", 10, Theme.Text, FontStyle.Bold), 0, 0);
        var timings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Margin = Padding.Empty };
        timings.ColumnStyles.Add(new(SizeType.Percent, 50));
        timings.ColumnStyles.Add(new(SizeType.Absolute, 12));
        timings.ColumnStyles.Add(new(SizeType.Percent, 50));
        timings.RowStyles.Add(new(SizeType.Percent, 50));
        timings.RowStyles.Add(new(SizeType.Absolute, 10));
        timings.RowStyles.Add(new(SizeType.Percent, 50));
        timings.Controls.Add(interval, 0, 0);
        timings.Controls.Add(hold, 2, 0);
        var countField = Stack((SizeType.Absolute, 18), (SizeType.Absolute, 32));
        countField.Controls.Add(Caption("点击次数", 8.5f), 0, 0);
        limit.Dock = DockStyle.Fill;
        limit.Margin = Padding.Empty;
        limit.AccessibleName = "点击次数";
        countField.Controls.Add(limit, 0, 1);
        controlHints.SetToolTip(limit, "0 表示不限次数，可随时停止。");
        timings.Controls.Add(countField, 0, 2);
        timings.Controls.Add(delay, 2, 2);
        settings.AddRange([interval, hold, delay, limit]);
        rhythmLayout.Controls.Add(timings, 0, 1);
        left.Controls.Add(rhythm, 0, 2);

        var offsetCard = Card();
        offsetCard.Padding = new(14);
        randomOffset.Font = new(Font.FontFamily, 10, FontStyle.Bold);
        var offsetLayout = Stack((SizeType.Absolute, 32), (SizeType.Absolute, 62));
        offsetCard.Controls.Add(offsetLayout);
        offsetLayout.Controls.Add(randomOffset, 0, 0);
        var ranges = Grid(3, 12);
        ranges.Controls.Add(Field("横向范围 ±", offsetX), 0, 0);
        ranges.Controls.Add(Field("纵向范围 ±", offsetY), 2, 0);
        offsetLayout.Controls.Add(ranges, 0, 1);
        controlHints.SetToolTip(randomOffset, "围绕点击前的鼠标位置偏移，点击后回到原位。");
        controlHints.SetToolTip(offsetX, "鼠标随机偏移的水平范围，单位：屏幕像素。");
        controlHints.SetToolTip(offsetY, "鼠标随机偏移的垂直范围，单位：屏幕像素。");
        right.Controls.Add(offsetCard, 0, 0);
        randomOffset.CheckedChanged += (_, _) => UpdateOffsetState();
        settings.Add(randomOffset);

        var protection = Card();
        protection.Padding = new(14);
        var protectionLayout = Stack((SizeType.Absolute, 22), (SizeType.Absolute, 28),
            (SizeType.Percent, 100));
        protection.Controls.Add(protectionLayout);
        protectionLayout.Controls.Add(Caption("快捷键与保护", 10, Theme.Text, FontStyle.Bold), 0, 0);
        protectionLayout.Controls.Add(guard, 0, 1);
        protectionLayout.Controls.Add(editHotKeys, 0, 2);
        right.Controls.Add(protection, 0, 2);
        settings.AddRange([guard, editHotKeys]);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2,
            Margin = new(0, 12, 0, 0) };
        footer.ColumnStyles.Add(new(SizeType.Absolute, 124));
        footer.ColumnStyles.Add(new(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new(SizeType.Absolute, 292));
        footer.RowStyles.Add(new(SizeType.Absolute, 58));
        footer.RowStyles.Add(new(SizeType.Percent, 100));
        footer.Controls.Add(counter, 0, 0);
        badge.Dock = DockStyle.Fill;
        badge.TextAlign = ContentAlignment.MiddleLeft;
        footer.Controls.Add(badge, 1, 0);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new(SizeType.Percent, 65));
        actions.ColumnStyles.Add(new(SizeType.Absolute, 10));
        actions.ColumnStyles.Add(new(SizeType.Percent, 35));
        actions.RowStyles.Add(new(SizeType.Absolute, 38));
        actions.RowStyles.Add(new(SizeType.Percent, 100));
        start.Margin = stop.Margin = Padding.Empty;
        actions.Controls.Add(start, 0, 0);
        actions.Controls.Add(stop, 2, 0);
        shortcuts.Dock = DockStyle.Fill;
        shortcuts.AutoEllipsis = true;
        shortcuts.TextAlign = ContentAlignment.MiddleRight;
        actions.Controls.Add(shortcuts, 0, 1);
        actions.SetColumnSpan(shortcuts, 3);
        footer.Controls.Add(actions, 2, 0);
        state.Dock = DockStyle.Fill;
        state.Margin = new(0, 4, 0, 0);
        footer.Controls.Add(state, 0, 1);
        footer.SetColumnSpan(state, 3);
        root.Controls.Add(footer, 0, 2);

        mouseTab.Click += (_, _) => SelectClickerMode(ClickMode.LeftMouse);
        keyboardTab.Click += (_, _) => SelectClickerMode(ClickMode.Keyboard);
        queueTab.Click += (_, _) => SelectQueuePage(true);
        start.Click += (_, _) => Toggle();
        stop.Click += (_, _) => cancellation?.Cancel();
        editHotKeys.Click += (_, _) => EditHotKeys();
        recordButton.Click += (_, _) => ToggleRecording();
        editRecordingHotKeyButton.Click += (_, _) => EditRecordingHotKey();
        renameRecordingButton.Click += (_, _) => RenameSelectedRecording();
        deleteRecordingButton.Click += (_, _) => DeleteSelectedRecording();
        importRecordingsButton.Click += (_, _) => ImportRecordings();
        exportRecordingButton.Click += (_, _) => ExportSelectedRecording();
        addToQueueButton.Click += (_, _) => AddSelectedRecordingToQueue();
        queueUpButton.Click += (_, _) => MoveQueueSelection(-1);
        queueDownButton.Click += (_, _) => MoveQueueSelection(1);
        queueRemoveButton.Click += (_, _) => RemoveQueueSelection();
        recordingList.SelectedIndexChanged += (_, _) => RefreshQueueControls();
        queueList.SelectedIndexChanged += (_, _) => RefreshQueueControls();
        queueInterval.ValueChanged += (_, _) => SaveCurrentSettings();
        loopCount.ValueChanged += (_, _) => SaveCurrentSettings();
        loopInterval.ValueChanged += (_, _) => SaveCurrentSettings();
        pathPerturbation.CheckedChanged += (_, _) => { pathPerturbationPixels.Enabled = pathPerturbation.Checked; SaveCurrentSettings(); };
        pathPerturbationPixels.ValueChanged += (_, _) => SaveCurrentSettings();
        if (!preview)
        {
            AppStorage.Prepare();
            LoadSettings();
            recordings.AddRange(RecordingLibrary.Load());
            var knownRecordingIds = recordings.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            queueIds.RemoveAll(x => !knownRecordingIds.Contains(x));
            RefreshRecordingLists();
        }
        else { legacyScheme = 1; toggleBinding = GlobalHotKeys.Preset(1); }
        interval.ValueChanged += (_, _) => RefreshTimingValidation();
        hold.ValueChanged += (_, _) => RefreshTimingValidation();
        delay.ValueChanged += (_, _) => RefreshTimingValidation();
        key.SelectedIndexChanged += (_, _) => RefreshTimingValidation();
        ready = true;
        SetMode(selectedMode);
        SelectQueuePage(false);
        FormClosing += OnClosing;
        Shown += (_, _) => RefreshNumberLayouts();
        ResumeLayout(true);
        ApplyInitialBackColors(this, Theme.Background);
        PrepareControlHandles(clickerPage);
        PrepareControlHandles(recordingPage);
    }

    private static void ApplyInitialBackColors(Control control, Color inherited)
    {
        Color background = control switch
        {
            Form => Theme.Background,
            SurfacePanel or ToggleSwitch => Theme.Surface,
            DarkNumber or DarkListBox or ModernButton => Theme.Input,
            Label => Color.Transparent,
            _ => inherited
        };
        control.BackColor = background;
        foreach (Control child in control.Controls) ApplyInitialBackColors(child, background);
    }

    private static void PrepareControlHandles(Control? root)
    {
        if (root is null || root.IsDisposed) return;
        _ = root.Handle;
        foreach (Control child in root.Controls) PrepareControlHandles(child);
    }

    private static Label Caption(string text, float size, Color? color = null, FontStyle style = FontStyle.Regular) =>
        new() { Text = text, ForeColor = color ?? Theme.Muted, Font = new("Microsoft YaHei UI", size, style),
            AutoSize = false, Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.Transparent };
    private static SurfacePanel Card() => new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private Control BuildRecordingPage()
    {
        var page = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        page.ColumnStyles.Add(new(SizeType.Percent, 51));
        page.ColumnStyles.Add(new(SizeType.Absolute, 14));
        page.ColumnStyles.Add(new(SizeType.Percent, 49));
        page.RowStyles.Add(new(SizeType.Percent, 100));

        var libraryCard = Card();
        libraryCard.Padding = new(12);
        var libraryLayout = Stack((SizeType.Absolute, 24), (SizeType.Percent, 100),
            (SizeType.Absolute, 34), (SizeType.Absolute, 34), (SizeType.Absolute, 34),
            (SizeType.Absolute, 34));
        libraryCard.Controls.Add(libraryLayout);
        libraryLayout.Controls.Add(Caption("录制库", 10, Theme.Text, FontStyle.Bold), 0, 0);
        recordingList.IntegralHeight = false;
        libraryLayout.Controls.Add(recordingList, 0, 1);
        recordButton.Margin = Padding.Empty;
        libraryLayout.Controls.Add(recordButton, 0, 2);
        libraryLayout.Controls.Add(Horizontal(renameRecordingButton, deleteRecordingButton), 0, 3);
        libraryLayout.Controls.Add(Horizontal(importRecordingsButton, exportRecordingButton), 0, 4);
        libraryLayout.Controls.Add(Horizontal(addToQueueButton, editRecordingHotKeyButton), 0, 5);

        var queueCard = Card();
        queueCard.Padding = new(12);
        var queueLayout = Stack((SizeType.Absolute, 24), (SizeType.Percent, 100),
            (SizeType.Absolute, 34), (SizeType.Absolute, 48), (SizeType.Absolute, 48));
        queueCard.Controls.Add(queueLayout);
        queueLayout.Controls.Add(Caption("执行队列", 10, Theme.Text, FontStyle.Bold), 0, 0);
        queueList.IntegralHeight = false;
        queueLayout.Controls.Add(queueList, 0, 1);
        queueLayout.Controls.Add(Horizontal(queueUpButton, queueDownButton, queueRemoveButton), 0, 2);
        queueInterval.AccessibleName = "队列间隔毫秒";
        loopCount.AccessibleName = "队列循环次数，零为无限";
        loopInterval.AccessibleName = "循环间隔毫秒";
        controlHints.SetToolTip(queueInterval, "上一段结束后等待的时间；鼠标转场计入间隔。单位：毫秒。");
        controlHints.SetToolTip(loopCount, "完整播放队列的轮数；0 表示无限循环。");
        controlHints.SetToolTip(loopInterval, "每轮队列结束后等待的时间；返回下一轮首段的鼠标转场计入间隔。单位：毫秒。");
        controlHints.SetToolTip(pathPerturbation, "每次回放随机扰动移动路线中段，起点和终点不变。");
        controlHints.SetToolTip(pathPerturbationPixels, "路径最大扰动幅度：0–20 像素。");
        queueLayout.Controls.Add(Horizontal(NumberField("片段间隔 ms", queueInterval),
            NumberField("循环次数（0 无限）", loopCount)), 0, 3);
        queueLayout.Controls.Add(Horizontal(NumberField("循环间隔 ms", loopInterval),
            JitterField()), 0, 4);
        page.Controls.Add(libraryCard, 0, 0);
        page.Controls.Add(queueCard, 2, 0);
        queueControls.AddRange([recordingList, queueList, recordButton, renameRecordingButton, deleteRecordingButton,
            importRecordingsButton, exportRecordingButton, addToQueueButton, editRecordingHotKeyButton,
            queueUpButton, queueDownButton, queueRemoveButton, queueInterval, loopCount, loopInterval,
            pathPerturbation, pathPerturbationPixels]);
        return page;
    }

    private static TableLayoutPanel Horizontal(params Control[] controls)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = controls.Length, RowCount = 1, Margin = Padding.Empty };
        for (int i = 0; i < controls.Length; i++)
        {
            row.ColumnStyles.Add(new(SizeType.Percent, 100f / controls.Length));
            controls[i].Dock = DockStyle.Fill;
            controls[i].Margin = new(i == 0 ? 0 : 4, 0, i == controls.Length - 1 ? 0 : 4, 0);
            row.Controls.Add(controls[i], i, 0);
        }
        row.RowStyles.Add(new(SizeType.Percent, 100));
        return row;
    }

    private Control NumberField(string title, DarkNumber number)
    {
        var field = Stack((SizeType.Absolute, 17), (SizeType.Percent, 100));
        number.Dock = DockStyle.Fill;
        number.Margin = Padding.Empty;
        field.Controls.Add(Caption(title, 8), 0, 0);
        field.Controls.Add(number, 0, 1);
        return field;
    }

    private Control JitterField()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        row.ColumnStyles.Add(new(SizeType.Percent, 100));
        row.ColumnStyles.Add(new(SizeType.Absolute, 76));
        row.RowStyles.Add(new(SizeType.Percent, 100));
        pathPerturbation.AccessibleName = "启用鼠标移动路径扰动";
        pathPerturbation.Margin = new(0, 0, 4, 0);
        pathPerturbationPixels.AccessibleName = "鼠标移动路径扰动像素";
        pathPerturbationPixels.Dock = DockStyle.Fill;
        pathPerturbationPixels.Margin = Padding.Empty;
        row.Controls.Add(pathPerturbation, 0, 0);
        row.Controls.Add(pathPerturbationPixels, 1, 0);
        return row;
    }

    private static TableLayoutPanel Stack(params (SizeType Type, float Size)[] rows)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length, Margin = Padding.Empty };
        grid.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var row in rows) grid.RowStyles.Add(new(row.Type, row.Size));
        return grid;
    }
    private static TableLayoutPanel Column(int first, int gap) =>
        Stack((SizeType.Absolute, first), (SizeType.Absolute, gap), (SizeType.Percent, 100));
    private static TableLayoutPanel Grid(int columns, int gap = 0)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = 1, Margin = Padding.Empty };
        for (int i = 0; i < columns; i++) grid.ColumnStyles.Add(gap > 0 && i == 1
            ? new(SizeType.Absolute, gap) : new(SizeType.Percent, 100f / (gap > 0 ? columns - 1 : columns)));
        grid.RowStyles.Add(new(SizeType.Percent, 100));
        return grid;
    }
    private Control Field(string name, DarkNumber number)
    {
        var field = Stack((SizeType.Absolute, 24), (SizeType.Percent, 100));
        number.AccessibleName = name;
        number.Dock = DockStyle.Fill;
        number.Margin = Padding.Empty;
        field.Controls.Add(Caption(name, 8.5f), 0, 0);
        field.Controls.Add(number, 0, 1);
        if (!settings.Contains(number)) settings.Add(number);
        return field;
    }
    private void SetMode(ClickMode mode)
    {
        ApplyMode(mode);
        RefreshTimingValidation();
    }

    private void ApplyMode(ClickMode mode)
    {
        selectedMode = mode;
        bool keyboard = mode == ClickMode.Keyboard;
        mouseTab.Selected = !queuePageSelected && !keyboard;
        keyboardTab.Selected = !queuePageSelected && keyboard;
        inputTitle.Text = keyboard ? "键盘按键" : "鼠标按键";
        mousePicker.Visible = !keyboard;
        key.Visible = keyboard;
        key.Enabled = keyboard && cancellation == null;
        for (int i = 0; i < mouseButtons.Length; i++) mouseButtons[i].Selected = i == (int)mode;
        UpdateOffsetState();
    }

    private void SelectClickerMode(ClickMode mode)
    {
        if (!queuePageSelected && selectedMode == mode) return;
        SwitchPage(false, mode);
    }

    private void SelectQueuePage(bool selected)
    {
        if (queuePageSelected == selected) return;
        SwitchPage(selected, null);
    }

    private void SwitchPage(bool selected, ClickMode? mode)
    {
        SuspendLayout();
        pageHost?.SuspendLayout();
        clickerPage?.SuspendLayout();
        recordingPage?.SuspendLayout();
        queuePageSelected = selected;
        if (mode.HasValue) ApplyMode(mode.Value);
        if (clickerPage is not null) clickerPage.Visible = !selected;
        if (recordingPage is not null) recordingPage.Visible = selected;
        mouseTab.Selected = !selected && selectedMode != ClickMode.Keyboard;
        keyboardTab.Selected = !selected && selectedMode == ClickMode.Keyboard;
        queueTab.Selected = selected;
        start.Text = selected ? "开始回放" : "开始连点";
        RenderCounter();
        if (cancellation is null && activeRecorder is null)
        {
            bool hotKeyAvailable = hotKeysReady || preview;
            state.Text = hotKeyAvailable ? "" : "播放快捷键不可用";
            badge.Text = hotKeyAvailable ? "●  就绪" : "●  热键冲突";
            badge.ForeColor = hotKeyAvailable ? Theme.Green : Theme.Red;
        }
        clickerPage?.ResumeLayout(false);
        recordingPage?.ResumeLayout(false);
        pageHost?.ResumeLayout(false);
        ResumeLayout(false);
        RefreshTimingValidation();
    }

    private void ResizePages()
    {
        if (pageHost is null) return;
        var bounds = pageHost.ClientRectangle;
        if (clickerPage is not null && clickerPage.Bounds != bounds) clickerPage.Bounds = bounds;
        if (recordingPage is not null && recordingPage.Bounds != bounds) recordingPage.Bounds = bounds;
    }

    private void RefreshQueueControls()
    {
        if (ready)
        {
            bool busy = cancellation is not null || activeRecorder is not null;
            bool hasRecording = recordingList.SelectedItem is RecordingListItem;
            bool hasQueuedItem = queueList.SelectedIndex >= 0;
            foreach (var control in queueControls)
            {
                bool enabled = !busy;
                if (ReferenceEquals(control, recordButton)) enabled = cancellation is null;
                else if (ReferenceEquals(control, pathPerturbationPixels)) enabled &= pathPerturbation.Checked;
                else if (ReferenceEquals(control, addToQueueButton) || ReferenceEquals(control, renameRecordingButton)
                    || ReferenceEquals(control, deleteRecordingButton) || ReferenceEquals(control, exportRecordingButton))
                    enabled &= hasRecording;
                else if (ReferenceEquals(control, queueRemoveButton)) enabled &= hasQueuedItem;
                else if (ReferenceEquals(control, queueUpButton)) enabled &= hasQueuedItem && queueList.SelectedIndex > 0;
                else if (ReferenceEquals(control, queueDownButton))
                    enabled &= hasQueuedItem && queueList.SelectedIndex < queueIds.Count - 1;
                SetEnabledIfChanged(control, enabled);
            }
        }
        if (queuePageSelected)
            SetEnabledIfChanged(start, (hotKeysReady || preview) && queueIds.Count > 0 && cancellation == null
                && activeRecorder == null && !closing && !editingHotKeys);
    }

    private static void SetEnabledIfChanged(Control control, bool enabled)
    {
        if (control.Enabled != enabled) control.Enabled = enabled;
    }

    private void RefreshRecordingLists()
    {
        int selectedLibraryIndex = recordingList.SelectedIndex;
        int selectedQueueIndex = queueList.SelectedIndex;
        recordingList.BeginUpdate();
        queueList.BeginUpdate();
        recordingList.Items.Clear();
        foreach (var item in recordings.OrderByDescending(x => x.CreatedAtUtc))
            recordingList.Items.Add(new RecordingListItem(item.Id, item.Name, item.CreatedAtUtc, item.Events.Count));
        queueList.Items.Clear();
        for (int i = 0; i < queueIds.Count; i++)
        {
            var recording = recordings.FirstOrDefault(x => x.Id == queueIds[i]);
            if (recording is not null) queueList.Items.Add(new QueueListItem(queueIds[i], i + 1, recording.Name));
        }
        if (recordingList.Items.Count > 0) recordingList.SelectedIndex = Math.Clamp(selectedLibraryIndex, 0, recordingList.Items.Count - 1);
        if (queueList.Items.Count > 0) queueList.SelectedIndex = Math.Clamp(selectedQueueIndex, 0, queueList.Items.Count - 1);
        recordingList.EndUpdate();
        queueList.EndUpdate();
        RefreshQueueControls();
    }

    private RecordingData? SelectedRecording => recordingList.SelectedItem is RecordingListItem item
        ? recordings.FirstOrDefault(x => x.Id == item.Id) : null;

    private void AddSelectedRecordingToQueue()
    {
        if (SelectedRecording is not { } recording || cancellation is not null || activeRecorder is not null) return;
        queueIds.Add(recording.Id);
        RefreshRecordingLists();
        queueList.SelectedIndex = queueIds.Count - 1;
        SaveCurrentSettings();
    }

    private void MoveQueueSelection(int direction)
    {
        int index = queueList.SelectedIndex, target = index + direction;
        if (index < 0 || target < 0 || target >= queueIds.Count) return;
        (queueIds[index], queueIds[target]) = (queueIds[target], queueIds[index]);
        RefreshRecordingLists();
        queueList.SelectedIndex = target;
        SaveCurrentSettings();
    }

    private void RemoveQueueSelection()
    {
        int index = queueList.SelectedIndex;
        if (index < 0 || index >= queueIds.Count) return;
        queueIds.RemoveAt(index);
        RefreshRecordingLists();
        if (queueList.Items.Count > 0) queueList.SelectedIndex = Math.Min(index, queueList.Items.Count - 1);
        SaveCurrentSettings();
    }

    private void RenameSelectedRecording()
    {
        var selected = SelectedRecording;
        if (selected is null) return;
        using var dialog = new TextPromptDialog("录制改名", "名称", selected.Name);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string name = dialog.Value.Trim();
        if (name.Length == 0 || name.Length > 80)
        {
            MessageBox.Show(this, "名称不能为空，且最多 80 个字符。", "LightTap", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (recordings.Any(x => x.Id != selected.Id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "录制名称已存在，请使用其他名称。", "LightTap", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        int index = recordings.FindIndex(x => x.Id == selected.Id);
        recordings[index] = selected with { Name = name };
        PersistRecordingLibrary();
        RefreshRecordingLists();
        SaveCurrentSettings();
    }

    private void DeleteSelectedRecording()
    {
        var selected = SelectedRecording;
        if (selected is null) return;
        if (!ConfirmDialog.Confirm(this, "删除录制", $"确定删除录制“{selected.Name}”？\n此操作无法撤销。", "删除")) return;
        recordings.RemoveAll(x => x.Id == selected.Id);
        queueIds.RemoveAll(x => x == selected.Id);
        PersistRecordingLibrary();
        RefreshRecordingLists();
        SaveCurrentSettings();
    }

    private void ImportRecordings()
    {
        using var dialog = new OpenFileDialog { Title = "导入录制文件", Filter = "键鼠录制文件 (*.kkrec)|*.kkrec",
            Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        int imported = 0;
        var errors = new List<string>();
        foreach (string path in dialog.FileNames)
        {
            try
            {
                var item = RecordingLibrary.Import(path);
                item = item with { Name = RecordingLibrary.UniqueName(item.Name, recordings) };
                recordings.Add(item);
                imported++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            { errors.Add($"{Path.GetFileName(path)}：{error.Message}"); }
        }
        if (imported > 0) PersistRecordingLibrary();
        RefreshRecordingLists();
        SaveCurrentSettings();
        state.Text = errors.Count == 0 ? $"已导入 {imported} 份录制。" : $"导入 {imported} 份；{errors.Count} 份文件无法读取。";
        if (errors.Count > 0) MessageBox.Show(this, string.Join(Environment.NewLine, errors.Take(8)),
            "部分文件导入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ExportSelectedRecording()
    {
        var selected = SelectedRecording;
        if (selected is null) return;
        using var dialog = new SaveFileDialog { Title = "导出录制文件", Filter = "键鼠录制文件 (*.kkrec)|*.kkrec",
            AddExtension = true, DefaultExt = "kkrec", FileName = SafeFileName(selected.Name) + ".kkrec", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            RecordingLibrary.Export(selected, dialog.FileName);
            state.Text = $"已手动导出“{selected.Name}”。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { MessageBox.Show(this, error.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static string SafeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return name;
    }

    private void PersistRecordingLibrary()
    {
        try { RecordingLibrary.Save(recordings); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { state.Text = $"录制库保存失败：{error.Message}"; }
    }

    private void ToggleRecording()
    {
        if (preview || closing || editingHotKeys) return;
        if (cancellation is not null)
        {
            state.Text = "队列或连点正在运行，当前无法录制。";
            return;
        }
        if (activeRecorder is not null) { FinishRecording(); return; }
        activeRecordingCreatedAt = DateTime.UtcNow;
        activeRecordingName = RecordingLibrary.UniqueName($"录制 {DateTime.Now:yyyy-MM-dd HH-mm-ss}", recordings);
        try
        {
            activeRecorder = new WindowsInputRecorder(Handle, recordingBinding, toggleBinding);
            recordButton.Text = "结束并保存录制";
            recordButton.Danger = true;
            state.Text = $"正在录制“{activeRecordingName}” · 按 {recordingBinding} 结束。";
            badge.Text = "●  正在录制";
            badge.ForeColor = Theme.Accent;
            RefreshQueueControls();
        }
        catch (Exception error)
        {
            activeRecorder?.Dispose();
            activeRecorder = null;
            state.Text = error.Message;
            badge.Text = "●  录制失败";
            badge.ForeColor = Theme.Red;
            recordButton.Text = "开始录制";
            recordButton.Danger = false;
            RefreshQueueControls();
        }
    }

    private void FinishRecording()
    {
        var current = activeRecorder;
        if (current is null) return;
        activeRecorder = null;
        try
        {
            var saved = current.Finish(activeRecordingName, activeRecordingCreatedAt);
            recordings.Add(saved);
            PersistRecordingLibrary();
            RefreshRecordingLists();
            SaveCurrentSettings();
            state.Text = $"已保存“{saved.Name}”，共 {saved.Events.Count:N0} 个输入事件。";
            badge.Text = "●  已保存";
            badge.ForeColor = Theme.Green;
        }
        catch (Exception error)
        {
            current.Dispose();
            state.Text = $"结束录制失败：{error.Message}";
            badge.Text = "●  录制失败";
            badge.ForeColor = Theme.Red;
        }
        finally
        {
            recordButton.Text = "开始录制";
            recordButton.Danger = false;
            RefreshQueueControls();
        }
    }

    private void EditRecordingHotKey()
    {
        if (cancellation is not null || activeRecorder is not null || editingHotKeys || closing || preview) return;
        editingHotKeys = true;
        var previous = recordingBinding;
        GlobalHotKeys.Unregister(Handle, 2);
        recordingHotKeyReady = false;
        try
        {
            using var dialog = new HotKeyDialog(recordingBinding, null, "录制开始 / 结束");
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (dialog.Binding == toggleBinding)
            {
                MessageBox.Show(this, "录制快捷键不能与播放快捷键相同。", "快捷键冲突",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!GlobalHotKeys.RegisterSingle(Handle, dialog.Binding, 2, out string message))
            {
                MessageBox.Show(this, message, "快捷键不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            recordingBinding = dialog.Binding;
            customRecordingHotKey = true;
            recordingHotKeyReady = true;
            SaveCurrentSettings();
        }
        finally
        {
            if (!recordingHotKeyReady)
            {
                recordingBinding = previous;
                recordingHotKeyReady = GlobalHotKeys.RegisterSingle(Handle, recordingBinding, 2, out string error);
                recordingHotKeyIssue = recordingHotKeyReady ? "" : error;
            }
            editingHotKeys = false;
            UpdateHotKeyText();
            RefreshTimingValidation();
        }
    }
    private void UpdateOffsetState()
    {
        bool mouse = selectedMode != ClickMode.Keyboard;
        randomOffset.Enabled = mouse && cancellation == null;
        offsetX.Enabled = offsetY.Enabled = mouse && randomOffset.Checked && cancellation == null;
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int enabled = 1;
        _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
        if (preview) UpdateHotKeyText(); else BindHotKeys(true);
    }
    protected override bool ShowWithoutActivation => preview || base.ShowWithoutActivation;
    private void BindHotKeys(bool allowFallback)
    {
        hotKeysReady = GlobalHotKeys.RegisterSingle(Handle, toggleBinding, 1, out string message);
        if (!hotKeysReady && allowFallback && !customHotKeys)
            for (int i = 0; i < GlobalHotKeys.Names.Length; i++)
            {
                var candidate = GlobalHotKeys.Preset(i);
                if (i == legacyScheme || !GlobalHotKeys.RegisterSingle(Handle, candidate, 1, out _)) continue;
                legacyScheme = i;
                toggleBinding = candidate;
                hotKeysReady = true;
                break;
            }
        if (recordingBinding == toggleBinding)
        {
            recordingHotKeyReady = false;
            recordingHotKeyIssue = "录制快捷键与播放快捷键相同，请重新设置。";
        }
        else
        {
            recordingHotKeyReady = GlobalHotKeys.RegisterSingle(Handle, recordingBinding, 2, out string recordingMessage);
            recordingHotKeyIssue = recordingHotKeyReady ? "" : recordingMessage;
        }
        UpdateHotKeyText();
        start.Enabled = hotKeysReady;
        state.Text = hotKeysReady ? "" : message;
        badge.Text = hotKeysReady ? "●  就绪" : "●  热键冲突";
        badge.ForeColor = hotKeysReady ? Theme.Green : Theme.Red;
        RefreshTimingValidation();
    }
    private void UpdateHotKeyText()
    {
        shortcuts.Text = recordingHotKeyReady || preview
            ? $"{toggleBinding} 播放 / 停止 · {recordingBinding} 录制"
            : $"{toggleBinding} 播放 / 停止 · 录制键不可用";
        shortcuts.ForeColor = recordingHotKeyReady || preview ? Theme.Muted : Theme.Red;
        controlHints.SetToolTip(shortcuts, recordingHotKeyReady || preview
            ? "播放快捷键控制当前页面；录制快捷键开始或结束录制。"
            : recordingHotKeyIssue);
    }
    private void EditHotKeys()
    {
        if (cancellation != null || activeRecorder is not null || editingHotKeys || closing || preview) return;
        editingHotKeys = true;
        var previousToggle = toggleBinding;
        GlobalHotKeys.Unregister(Handle, 1);
        hotKeysReady = false;
        string? notice = null;
        try
        {
            using var dialog = new HotKeyDialog(toggleBinding,
                selectedMode == ClickMode.Keyboard ? ((KeyChoice)key.SelectedItem!).Key : null);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (dialog.Binding == recordingBinding)
            { notice = "播放快捷键不能与录制快捷键相同。"; return; }
            if (!GlobalHotKeys.RegisterSingle(Handle, dialog.Binding, 1, out string error))
            { notice = error + " 已尝试恢复原快捷键。"; return; }
            toggleBinding = dialog.Binding;
            customHotKeys = hotKeysReady = true;
            notice = SaveSettings(ReadOptions()) ? "自定义快捷键可用，已应用并保存。"
                : "自定义快捷键已应用，但配置目录不可写，未能保存。";
        }
        finally
        {
            if (!hotKeysReady)
            {
                toggleBinding = previousToggle;
                hotKeysReady = GlobalHotKeys.RegisterSingle(Handle, toggleBinding, 1, out string restoreError);
                if (!hotKeysReady) notice = restoreError + " 请重新设置快捷键。";
            }
            editingHotKeys = false;
            UpdateHotKeyText();
            badge.Text = hotKeysReady ? "●  就绪" : "●  热键冲突";
            badge.ForeColor = hotKeysReady ? Theme.Green : Theme.Red;
            state.Text = notice ?? (hotKeysReady ? "" : "快捷键不可用，请重新设置。");
            RefreshTimingValidation();
        }
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (!preview)
        {
            GlobalHotKeys.Unregister(Handle, 1);
            GlobalHotKeys.Unregister(Handle, 2);
        }
        base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == Native.HotKeyMessage && !editingHotKeys)
        {
            uint packed = unchecked((uint)message.LParam.ToInt64());
            int id = message.WParam.ToInt32();
            var binding = id == 2 ? recordingBinding : toggleBinding;
            bool matches = (packed >> 16) == (uint)binding.Key && (packed & 7) == binding.Modifiers;
            if (matches && id == 1) Toggle();
            else if (matches && id == 2 && recordingHotKeyReady) ToggleRecording();
        }
        base.WndProc(ref message);
    }
    private void Toggle()
    {
        if (cancellation != null) { cancellation.Cancel(); return; }
        if (activeRecorder is not null || !hotKeysReady || closing || preview || editingHotKeys) return;
        if (queuePageSelected) { BeginQueuePlayback(); return; }
        BeginClickerPlayback();
    }

    private void BeginClickerPlayback()
    {
        var options = ReadOptions();
        string? invalid = TimingPolicy.Validate(options) ?? GlobalHotKeys.InputConflict(options);
        if (invalid != null) { RefreshTimingValidation(); return; }
        SaveSettings(options);
        cancellation = new();
        foreach (var control in settings) control.Enabled = false;
        start.Enabled = false;
        start.Text = "运行中…";
        stop.Enabled = true;
        badge.Text = "●  准备开始";
        badge.ForeColor = Theme.Accent;
        UpdateCounter(0);
        running = ExecuteAsync(options, cancellation);
    }

    private void BeginQueuePlayback()
    {
        if (queueIds.Count == 0)
        {
            state.Text = "请先将录制数据加入播放队列。";
            return;
        }
        var queue = queueIds.Select(id => recordings.FirstOrDefault(x => x.Id == id))
            .Where(x => x is not null).Cast<RecordingData>().ToArray();
        if (queue.Length != queueIds.Count)
        {
            state.Text = "队列中包含已删除的录制，请检查队列。";
            return;
        }
        var playback = ReadPlaybackSettings();
        if (playback.LoopCount is < 0 or > 1_000_000 || playback.QueueIntervalMs is < 0 or > 3_600_000
            || playback.LoopIntervalMs is < 0 or > 3_600_000 || playback.PathPerturbationPixels is < 0 or > 20)
        {
            state.Text = "播放设置超出范围，请检查循环次数、间隔和路径扰动。";
            return;
        }
        SaveCurrentSettings();
        cancellation = new();
        foreach (var control in settings) control.Enabled = false;
        foreach (var control in queueControls) control.Enabled = false;
        start.Enabled = false;
        start.Text = "播放中…";
        stop.Enabled = true;
        badge.Text = "●  队列准备中";
        badge.ForeColor = Theme.Accent;
        UpdateCounter(0);
        Hide();
        running = ExecuteQueueAsync(queue, playback, cancellation);
    }

    private async Task ExecuteQueueAsync(IReadOnlyList<RecordingData> queue, PlaybackSettings playback,
        CancellationTokenSource source)
    {
        try
        {
            var progress = new Progress<string>(text =>
            {
                state.Text = text;
                if (text.StartsWith("正在")) { badge.Text = "●  正在播放"; badge.ForeColor = Theme.Green; }
            });
            var result = await Task.Run(() => RecordingPlayback.PlayAsync(queue, playback, progress, source.Token));
            UpdateCounter(result.CompletedLoops);
            state.Text = result.Reason;
            badge.Text = "●  播放结束";
            badge.ForeColor = Theme.Muted;
        }
        catch (Exception error)
        {
            state.Text = error.Message;
            badge.Text = "●  需要检查";
            badge.ForeColor = Theme.Red;
        }
        finally
        {
            cancellation = null;
            source.Dispose();
            if (!closing)
            {
                Show();
                WindowState = FormWindowState.Normal;
            }
            foreach (var control in settings) control.Enabled = true;
            foreach (var control in queueControls) control.Enabled = true;
            start.Text = queuePageSelected ? "开始回放" : "开始连点";
            stop.Enabled = false;
            SetMode(selectedMode);
            RefreshTimingValidation();
            RefreshQueueControls();
        }
    }
    private async Task ExecuteAsync(ClickOptions options, CancellationTokenSource source)
    {
        try
        {
            var progress = new Progress<string>(text =>
            {
                state.Text = text;
                if (text.StartsWith("正在")) { badge.Text = "●  正在连点"; badge.ForeColor = Theme.Green; }
            });
            int currentCount = 0;
            var counts = new InlineProgress<int>(value => Volatile.Write(ref currentCount, value));
            using var refresh = new System.Windows.Forms.Timer { Interval = 100 };
            refresh.Tick += (_, _) => UpdateCounter(Volatile.Read(ref currentCount));
            refresh.Start();
            nint ownWindow = Handle;
            var result = await Task.Run(() => ClickRunner.RunAsync(options, ownWindow, progress, counts, source.Token));
            refresh.Stop();
            UpdateCounter(result.Count);
            state.Text = result.Reason;
            badge.Text = "●  已停止";
            badge.ForeColor = Theme.Muted;
        }
        catch (Exception error) { state.Text = error.Message; badge.Text = "●  需要检查"; badge.ForeColor = Theme.Red; }
        finally
        {
            cancellation = null;
            source.Dispose();
            foreach (var control in settings) control.Enabled = true;
            SetMode(selectedMode);
            start.Text = queuePageSelected ? "开始回放" : "开始连点";
            RefreshTimingValidation();
            stop.Enabled = false;
            RefreshQueueControls();
        }
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (running is { IsCompleted: false })
        {
            e.Cancel = true;
            if (closing) return;
            closing = true;
            if (activeRecorder is not null) FinishRecording();
            cancellation?.Cancel();
            await running;
            Close();
        }
        else
        {
            if (activeRecorder is not null) FinishRecording();
            if (!preview && ready) SaveSettings(ReadOptions());
        }
    }
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var options = JsonSerializer.Deserialize<ClickOptions>(File.ReadAllText(ConfigPath));
            if (options == null || !Enum.IsDefined(options.Mode)) return;
            selectedMode = options.Mode;
            for (int i = 0; i < key.Items.Count; i++)
                if (((KeyChoice)key.Items[i]).Key == options.Key) key.SelectedIndex = i;
            interval.LoadValue(options.IntervalMs, options.IntervalVariation, TimeUnit.Milliseconds);
            hold.LoadValue(options.HoldMs, options.HoldVariation, TimeUnit.Milliseconds);
            limit.Value = options.Count;
            delay.LoadValue(options.StartDelayMs ?? Math.Clamp(options.DelaySeconds, 0, 30) * 1000,
                options.DelayVariation, TimeUnit.Seconds);
            guard.Checked = options.GuardWindow;
            legacyScheme = Math.Clamp(options.HotKeyScheme, 0, 2);
            toggleBinding = options.ToggleBinding;
            customHotKeys = options.ToggleHotKey != null;
            recordingBinding = options.RecordingBinding;
            customRecordingHotKey = options.RecordingHotKey != null;
            queueInterval.Value = Math.Clamp(options.QueueIntervalMs, 0, 3_600_000);
            loopCount.Value = Math.Clamp(options.LoopCount, 0, 1_000_000);
            loopInterval.Value = Math.Clamp(options.LoopIntervalMs, 0, 3_600_000);
            pathPerturbation.Checked = options.PathPerturbation;
            pathPerturbationPixels.Value = Math.Clamp(options.PathPerturbationPixels, 0, 20);
            pathPerturbationPixels.Enabled = pathPerturbation.Checked;
            queueIds.Clear();
            if (options.QueueIds is not null) queueIds.AddRange(options.QueueIds.Where(x => !string.IsNullOrWhiteSpace(x)));
            randomOffset.Checked = options.RandomOffset;
            offsetX.Value = options.OffsetX;
            offsetY.Value = options.OffsetY;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { state.Text = "配置未能读取，已使用默认设置。"; }
    }
    private bool SaveSettings(ClickOptions options)
    {
        try
        {
            string temporary = ConfigPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, ConfigPath, true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { state.Text = "配置目录不可写，本次设置仅在内存中保留。"; return false; }
    }
    private void SaveCurrentSettings()
    {
        if (!ready || preview || activeRecorder is not null) return;
        SaveSettings(ReadOptions());
    }
    private void UpdateCounter(int count)
    {
        if (queuePageSelected) queueCount = count;
        else clickerCount = count;
        RenderCounter();
    }

    private void RenderCounter()
    {
        int count = queuePageSelected ? queueCount : clickerCount;
        counter.Text = queuePageSelected ? $"{count:N0} 轮" : $"{count:N0} 次";
        float size = count >= 100000 ? 14 : count >= 10000 ? 18 : 22;
        if (counter.Font.Size == size) return;
        var previous = counter.Font;
        counter.Font = new(counter.Font.FontFamily, size);
        previous.Dispose();
    }
    internal void SetPreviewState(bool keyboard, bool offset, bool error = false)
    {
        randomOffset.Checked = offset;
        SetMode(keyboard ? ClickMode.Keyboard : ClickMode.LeftMouse);
        toggleBinding = GlobalHotKeys.Preset(2);
        UpdateHotKeyText();
        if (error) { state.Text = "输入发送失败。请检查目标窗口权限；普通权限程序可能无法操作管理员窗口。"; badge.Text = "●  需要检查"; badge.ForeColor = Theme.Red; UpdateCounter(1000000); }
    }
    internal void SetQueuePagePreview(bool selected) => SelectQueuePage(selected);
    private void RefreshNumberLayouts()
    {
        foreach (var number in settings.OfType<DarkNumber>()) number.RefreshEditorLayout();
        foreach (var number in queueControls.OfType<DarkNumber>()) number.RefreshEditorLayout();
        foreach (var timing in settings.OfType<TimingEditor>()) timing.RefreshEditors();
    }
    private ClickOptions ReadOptions() => new(selectedMode, ((KeyChoice)key.SelectedItem!).Key,
        interval.Milliseconds, hold.Milliseconds, limit.Value, delay.Milliseconds / 1000,
        guard.Checked, legacyScheme, randomOffset.Checked, offsetX.Value, offsetY.Value,
        interval.Variation, hold.Variation, delay.Variation, delay.Milliseconds,
        customHotKeys ? toggleBinding : null, customRecordingHotKey ? recordingBinding : null,
        queueInterval.Value, loopCount.Value, loopInterval.Value, pathPerturbation.Checked,
        pathPerturbationPixels.Value, queueIds.ToArray());

    private PlaybackSettings ReadPlaybackSettings() => new(queueInterval.Value, loopCount.Value,
        loopInterval.Value, pathPerturbation.Checked, pathPerturbationPixels.Value);
    private void RefreshTimingValidation()
    {
        if (!ready || cancellation != null) return;
        if (queuePageSelected)
        {
            start.Text = "开始回放";
            RefreshQueueControls();
            return;
        }
        var options = ReadOptions();
        string? error = TimingPolicy.Validate(options) ?? GlobalHotKeys.InputConflict(options);
        bool overlap = error?.StartsWith("间隔最小") ?? false;
        interval.UpdateRange(overlap);
        hold.UpdateRange(overlap);
        delay.UpdateRange(false);
        SetEnabledIfChanged(start, (hotKeysReady || preview) && !closing && !editingHotKeys
            && error == null && activeRecorder is null);
        if (error != null)
        {
            state.Text = error;
            badge.Text = "●  检查设置";
            badge.ForeColor = Theme.Red;
        }
        else if (badge.Text == "●  检查设置")
        {
            state.Text = hotKeysReady || preview ? "" : "快捷键不可用，请重新设置。";
            badge.Text = hotKeysReady || preview ? "●  就绪" : "●  热键冲突";
            badge.ForeColor = hotKeysReady || preview ? Theme.Green : Theme.Red;
        }
        if (cancellation is null) start.Text = queuePageSelected ? "开始回放" : "开始连点";
        RefreshQueueControls();
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    private sealed record KeyChoice(Keys Key)
    {
        public override string ToString() => Key switch
        {
            >= Keys.D0 and <= Keys.D9 => ((char)Key).ToString(),
            Keys.Space => "Space · 空格", Keys.Enter => "Enter · 回车", Keys.Escape => "Esc",
            Keys.Back => "Backspace · 退格", _ => Key.ToString()
        };
    }
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
