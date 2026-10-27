using System.ComponentModel;

namespace KeyKing;

internal sealed class DarkListBox : ListBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string EmptyText { get; set; } = "";

    internal DarkListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 34;
        IntegralHeight = false;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Input;
        ForeColor = Theme.Text;
        Font = new("Microsoft YaHei UI", 9);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var background = selected ? Theme.AccentSurface : Theme.Input;
        using var fill = new SolidBrush(background);
        e.Graphics.FillRectangle(fill, e.Bounds);
        string text = Items[e.Index]?.ToString() ?? "";
        var color = selected ? Theme.Accent : Theme.Text;
        TextRenderer.DrawText(e.Graphics, text, Font,
            new Rectangle(e.Bounds.Left + 10, e.Bounds.Top, e.Bounds.Width - 18, e.Bounds.Height), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if ((e.State & DrawItemState.Focus) != 0)
            ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, Theme.Accent, background);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg != 0x000F || Items.Count != 0 || string.IsNullOrEmpty(EmptyText) || !IsHandleCreated) return;
        using var graphics = Graphics.FromHwnd(Handle);
        TextRenderer.DrawText(graphics, EmptyText, Font, ClientRectangle, Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

internal sealed record RecordingListItem(string Id, string Name, DateTime CreatedAtUtc, int EventCount)
{
    public override string ToString() => $"{Name}  ·  {EventCount:N0} 个事件";
}

internal sealed record QueueListItem(string Id, int Order, string Name)
{
    public override string ToString() => $"{Order:00}  {Name}";
}

internal sealed class TextPromptDialog : Form
{
    private readonly TextBox editor = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.Input, ForeColor = Theme.Text, MaxLength = 80 };
    internal string Value => editor.Text;

    internal TextPromptDialog(string title, string prompt, string initialValue)
    {
        Text = title;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new("Microsoft YaHei UI", 9.5f);
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(440, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 3 };
        grid.ColumnStyles.Add(new(SizeType.Percent, 100));
        grid.RowStyles.Add(new(SizeType.Absolute, 28));
        grid.RowStyles.Add(new(SizeType.Absolute, 40));
        grid.RowStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(grid);
        grid.Controls.Add(new Label { Text = prompt, ForeColor = Theme.Muted, Dock = DockStyle.Fill }, 0, 0);
        editor.Text = initialValue;
        grid.Controls.Add(editor, 0, 1);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        actions.ColumnStyles.Add(new(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new(SizeType.Absolute, 96));
        actions.ColumnStyles.Add(new(SizeType.Absolute, 10));
        actions.ColumnStyles.Add(new(SizeType.Absolute, 118));
        var cancel = new ModernButton { Text = "取消", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel };
        var confirm = new ModernButton { Text = "保存", Primary = true, Dock = DockStyle.Fill, DialogResult = DialogResult.OK };
        actions.Controls.Add(cancel, 1, 0);
        actions.Controls.Add(confirm, 3, 0);
        grid.Controls.Add(actions, 0, 2);
        CancelButton = cancel;
        AcceptButton = confirm;
        Shown += (_, _) => { editor.Focus(); editor.SelectAll(); };
    }
}

internal sealed class ConfirmDialog : Form
{
    private ConfirmDialog(string title, string message, string confirmText)
    {
        Text = title;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new("Microsoft YaHei UI", 9.5f);
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(460, 216);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;

        var surface = new SurfacePanel { Dock = DockStyle.Fill, Padding = new(18) };
        Controls.Add(surface);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 34));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 40));
        surface.Controls.Add(layout);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.Absolute, 34));
        header.RowStyles.Add(new(SizeType.Percent, 100));
        header.Controls.Add(new Label { Text = title, ForeColor = Theme.Text, Font = new(Font, FontStyle.Bold),
            Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 0, 0);
        var close = new ModernButton { Text = "×", AccessibleName = "取消并关闭", Dock = DockStyle.Fill,
            Margin = Padding.Empty, DialogResult = DialogResult.Cancel, Font = new(Font.FontFamily, 16) };
        header.Controls.Add(close, 1, 0);
        layout.Controls.Add(header, 0, 0);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        content.ColumnStyles.Add(new(SizeType.Absolute, 42));
        content.ColumnStyles.Add(new(SizeType.Absolute, 12));
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        content.RowStyles.Add(new(SizeType.Percent, 100));
        var warning = new Label { Text = "!", AccessibleName = "警告", Dock = DockStyle.Fill, Margin = Padding.Empty,
            ForeColor = Color.FromArgb(244, 196, 91), Font = new(Font.FontFamily, 22, FontStyle.Bold),
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter };
        content.Controls.Add(warning, 0, 0);
        content.Controls.Add(new Label { Text = message, AccessibleName = "确认内容", Dock = DockStyle.Fill,
            ForeColor = Theme.Text, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 2, 0);
        layout.Controls.Add(content, 0, 1);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new(SizeType.Absolute, 96));
        actions.ColumnStyles.Add(new(SizeType.Absolute, 10));
        actions.ColumnStyles.Add(new(SizeType.Absolute, 96));
        actions.RowStyles.Add(new(SizeType.Percent, 100));
        var cancel = new ModernButton { Text = "取消", Dock = DockStyle.Fill, Margin = Padding.Empty,
            DialogResult = DialogResult.Cancel };
        var confirm = new ModernButton { Text = confirmText, Danger = true, Dock = DockStyle.Fill,
            Margin = Padding.Empty, DialogResult = DialogResult.Yes };
        actions.Controls.Add(cancel, 1, 0);
        actions.Controls.Add(confirm, 3, 0);
        layout.Controls.Add(actions, 0, 2);

        CancelButton = cancel;
        AcceptButton = cancel;
        Shown += (_, _) => cancel.Focus();
    }

    internal static bool Confirm(IWin32Window owner, string title, string message, string confirmText)
    {
        using var dialog = new ConfirmDialog(title, message, confirmText);
        return dialog.ShowDialog(owner) == DialogResult.Yes;
    }
}
