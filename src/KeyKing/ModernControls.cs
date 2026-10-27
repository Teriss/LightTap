using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace KeyKing;

internal static class Theme
{
    internal static readonly Color Background = Color.FromArgb(16, 18, 24);
    internal static readonly Color Surface = Color.FromArgb(25, 28, 37);
    internal static readonly Color Input = Color.FromArgb(18, 21, 29);
    internal static readonly Color Border = Color.FromArgb(47, 52, 66);
    internal static readonly Color Text = Color.FromArgb(235, 238, 247);
    internal static readonly Color Muted = Color.FromArgb(145, 153, 173);
    internal static readonly Color Accent = Color.FromArgb(155, 131, 255);
    internal static readonly Color AccentSurface = Color.FromArgb(47, 39, 72);
    internal static readonly Color Green = Color.FromArgb(111, 211, 166);
    internal static readonly Color Red = Color.FromArgb(244, 137, 152);
    internal static int Px(Control c, int value) => (int)Math.Round(value * c.DeviceDpi / 96d);
    internal static GraphicsPath Round(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
        if (diameter <= 0) { path.AddRectangle(rectangle); return path; }
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class SurfacePanel : Panel
{
    internal SurfacePanel()
    {
        DoubleBuffered = true;
        BackColor = Theme.Surface;
        Padding = new(18);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        using var path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Theme.Px(this, 14));
        using var fill = new SolidBrush(BackColor);
        using var border = new Pen(Theme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        base.OnPaint(e);
    }
}

internal sealed class BufferedPanel : Panel
{
    internal BufferedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
    }
}

internal sealed class BufferedTableLayoutPanel : TableLayoutPanel
{
    internal BufferedTableLayoutPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
    }
}

internal class ModernButton : Button
{
    private bool hovered;
    private bool pressed;
    private bool selected;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Selected { get => selected; set { selected = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Danger { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Dropdown { get; set; }
    internal ModernButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Input;
        ForeColor = Theme.Text;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        var fillColor = !Enabled ? Theme.Surface : Primary ? Theme.Accent : selected
            ? Theme.AccentSurface : hovered ? Color.FromArgb(38, 42, 55) : Theme.Input;
        if (pressed && Enabled) fillColor = Color.FromArgb(71, 59, 106);
        var textColor = !Enabled ? Color.FromArgb(94, 101, 120) : Primary ? Theme.Background
            : Danger ? Theme.Red : selected ? Theme.Accent : Theme.Text;
        using var path = Theme.Round(new Rectangle(1, 1, Width - 3, Height - 3), Theme.Px(this, 8));
        using var fill = new SolidBrush(fillColor);
        using var border = new Pen(Focused && ShowFocusCues || selected ? Theme.Accent : Theme.Border,
            Focused && ShowFocusCues ? 2 : 1);
        g.FillPath(fill, path);
        if (!Primary || Focused) g.DrawPath(border, path);
        var bounds = new Rectangle(Theme.Px(this, 12), 0, Width - Theme.Px(this, Dropdown ? 40 : 24), Height);
        TextRenderer.DrawText(g, Text, Font, bounds, textColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
            | (Dropdown ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
        if (Dropdown)
        {
            int x = Width - Theme.Px(this, 20), y = Height / 2;
            using var pen = new Pen(Enabled ? Theme.Muted : Theme.Border, Math.Max(1, DeviceDpi / 96f));
            g.DrawLines(pen, [new(x - Theme.Px(this, 3), y - 1), new(x, y + Theme.Px(this, 2)),
                new(x + Theme.Px(this, 3), y - 1)]);
        }
    }
}

internal sealed class ToggleSwitch : CheckBox
{
    internal ToggleSwitch()
    {
        AutoSize = false;
        Height = 28;
        Cursor = Cursors.Hand;
        ForeColor = Theme.Text;
        BackColor = Theme.Surface;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        int w = Theme.Px(this, 34), h = Theme.Px(this, 20);
        var track = new Rectangle(Width - w - 1, (Height - h) / 2, w, h);
        using var path = Theme.Round(track, h / 2);
        using var fill = new SolidBrush(Enabled && Checked ? Theme.Accent : Theme.Border);
        g.FillPath(fill, path);
        int knob = h - Theme.Px(this, 6);
        int x = Checked ? track.Right - knob - Theme.Px(this, 3) : track.Left + Theme.Px(this, 3);
        using var thumb = new SolidBrush(Enabled ? Theme.Text : Theme.Muted);
        g.FillEllipse(thumb, x, track.Top + Theme.Px(this, 3), knob, knob);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width - w - Theme.Px(this, 12), Height),
            Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, ClientRectangle, Theme.Accent, BackColor);
    }
}

internal sealed class DarkNumber : UserControl
{
    private readonly TextBox editor = new() { BorderStyle = BorderStyle.None, BackColor = Theme.Input,
        ForeColor = Theme.Text, Text = "0" };
    private readonly string suffix;
    private int value;
    private int maximum;
    internal int Minimum { get; }
    internal int Maximum => maximum;
    internal event EventHandler? ValueChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int Value
    {
        get { Commit(); return value; }
        set
        {
            this.value = Math.Clamp(value, Minimum, Maximum);
            editor.Text = this.value.ToString();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    internal DarkNumber(int initial, int minimum, int maximum, string suffix)
    {
        Minimum = minimum;
        this.maximum = maximum;
        BackColor = Theme.Input;
        Height = 38;
        Padding = new(12, 8, 12, 8);
        this.suffix = suffix;
        Controls.Add(editor);
        Value = initial;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        editor.GotFocus += (_, _) => Invalidate();
        editor.LostFocus += (_, _) => { Commit(); Invalidate(); };
        editor.TextChanged += (_, _) =>
        {
            if (int.TryParse(editor.Text, out int parsed) && parsed >= Minimum && parsed <= Maximum)
            { value = parsed; ValueChanged?.Invoke(this, EventArgs.Empty); }
        };
        editor.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar)) e.Handled = true; };
        editor.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Up or Keys.Down)
            { Value += e.KeyCode == Keys.Up ? 1 : -1; e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
        };
        Click += (_, _) => editor.Focus();
    }
    private void Commit()
    {
        if (long.TryParse(editor.Text, out long parsed)) value = (int)Math.Clamp(parsed, Minimum, Maximum);
        if (editor.Text != value.ToString()) editor.Text = value.ToString();
    }
    internal void SetMaximum(int maximum)
    {
        this.maximum = Math.Max(Minimum, maximum);
        Value = Math.Min(Value, this.maximum);
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int inset = Theme.Px(this, 12), suffixWidth = string.IsNullOrEmpty(suffix) ? 0 : Theme.Px(this, 30);
        editor.SetBounds(inset, Math.Max(0, (Height - editor.PreferredHeight) / 2),
            Math.Max(1, Width - inset * 2 - suffixWidth), editor.PreferredHeight);
    }
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformLayout();
    }
    internal void RefreshEditorLayout() => PerformLayout();
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
        using var path = Theme.Round(new Rectangle(1, 1, Width - 3, Height - 3), Theme.Px(this, 8));
        using var fill = new SolidBrush(Theme.Input);
        using var border = new Pen(ContainsFocus ? Theme.Accent : Theme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        int suffixWidth = Theme.Px(this, 30), inset = Theme.Px(this, 12);
        TextRenderer.DrawText(e.Graphics, suffix, Font,
            new Rectangle(Width - inset - suffixWidth, 2, suffixWidth, Height - 4),
            Enabled ? Theme.Muted : Color.FromArgb(94, 101, 120),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal sealed class DarkSelect : ModernButton
{
    internal List<object> Items { get; } = [];
    private int selectedIndex = -1;
    internal event EventHandler? SelectedIndexChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == selectedIndex) return;
            selectedIndex = value;
            Text = value < 0 ? "请选择" : Items[value].ToString();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    internal object? SelectedItem => selectedIndex < 0 ? null : Items[selectedIndex];
    internal DarkSelect()
    {
        Dropdown = true;
        Height = 38;
        AccessibleRole = AccessibleRole.ComboBox;
    }
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        var menu = new ToolStripDropDown { BackColor = Theme.Border, Padding = new(1), AutoSize = true };
        var list = new ListBox { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text,
            Font = Font, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.Px(this, 32), IntegralHeight = false,
            Width = Math.Max(Width - 2, Theme.Px(this, 140)), Height = Math.Min(Items.Count, 8) * Theme.Px(this, 32) };
        foreach (var item in Items) list.Items.Add(item);
        list.SelectedIndex = selectedIndex;
        list.DrawItem += (_, args) =>
        {
            if (args.Index < 0) return;
            bool active = (args.State & DrawItemState.Selected) != 0;
            using var fill = new SolidBrush(active ? Theme.AccentSurface : Theme.Input);
            args.Graphics.FillRectangle(fill, args.Bounds);
            var bounds = Rectangle.Inflate(args.Bounds, -Theme.Px(this, 12), 0);
            TextRenderer.DrawText(args.Graphics, Items[args.Index].ToString(), Font, bounds,
                active ? Theme.Accent : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        void Choose()
        {
            if (list.SelectedIndex >= 0) SelectedIndex = list.SelectedIndex;
            menu.Close();
        }
        list.MouseUp += (_, args) => { if (args.Button == MouseButtons.Left && list.IndexFromPoint(args.Location) >= 0) Choose(); };
        list.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter) { Choose(); args.Handled = true; }
            if (args.KeyCode == Keys.Escape) { menu.Close(); args.Handled = true; }
        };
        var host = new ToolStripControlHost(list) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = list.Size };
        menu.Items.Add(host);
        // Closed fires before ToolStrip finishes its close stack; dispose after it unwinds.
        menu.Closed += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)menu.Dispose);
        };
        menu.Show(this, new Point(0, Height));
        list.Focus();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Up or Keys.Down)
        {
            SelectedIndex = Math.Clamp(selectedIndex + (e.KeyCode == Keys.Down ? 1 : -1), 0, Items.Count - 1);
            e.Handled = true;
        }
        else base.OnKeyDown(e);
    }
}
