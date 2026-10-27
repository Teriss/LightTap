using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace KeyKing;

// Renders only our own window. No input injection, foreground activation or global hotkeys.
internal static class LayoutChecks
{
    internal static void Run(string output)
    {
        string path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var form = new MainForm(preview: true) { Opacity = 0, ShowInTaskbar = false };
        form.Shown += (_, _) => form.BeginInvoke(() =>
        {
            try
            {
                var snapshots = new List<object>();
                var switchTimes = new List<double>();
                bool success = true;
                void Snapshot(string name)
                {
                    form.PerformLayout();
                    var controls = Descendants(form)
                        .Where(c => c is Label or Button or ComboBox or ListBox or CheckBox or NumericUpDown or DarkNumber or TextBox)
                        .Select(c => new {
                            type = c.GetType().Name, text = c.Text, fontSize = c.Font.Size,
                            bounds = BoundsInForm(form, c), visible = c.Visible,
                            fitsParent = FitsAncestors(c, form),
                            requiredTextHeight = c is Label label
                                ? TextRenderer.MeasureText(label.Text, label.Font,
                                    new Size(label.Width, int.MaxValue), TextFormatFlags.WordBreak).Height : 0
                        }).ToArray();
                    bool fits = controls.Where(c => c.visible).All(c =>
                        new Rectangle(Point.Empty, form.ClientSize).Contains(c.bounds)
                        && c.bounds.Height >= c.requiredTextHeight && c.fitsParent);
                    success &= fits;
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    string imagePath = Path.Combine(Path.GetDirectoryName(path)!, $"lighttap-{name}.png");
                    bitmap.Save(imagePath, ImageFormat.Png);
                    snapshots.Add(new { name, fits, dpi = form.DeviceDpi, width = form.ClientSize.Width,
                        height = form.ClientSize.Height, imagePath, controls });
                }
                Snapshot("default");
                form.SetPreviewState(false, true);
                Snapshot("random-offset");
                form.Size = form.MinimumSize;
                Snapshot("minimum");
                form.SetPreviewState(true, true, true);
                success &= Descendants(form).OfType<DarkSelect>().First(c => c.AccessibleName == "键盘按键").Enabled;
                success &= !Descendants(form).OfType<ToggleSwitch>().First(c => c.AccessibleName == "启用鼠标随机位置偏移").Enabled;
                Snapshot("keyboard-error");
                var firstQueueSwitch = Stopwatch.StartNew();
                form.SetQueuePagePreview(true);
                FlushPendingPaint(form);
                firstQueueSwitch.Stop();
                double firstQueueSwitchMs = firstQueueSwitch.Elapsed.TotalMilliseconds;
                Snapshot("recording-queue");
                form.Size = form.MinimumSize;
                Snapshot("recording-queue-minimum");
                bool queueSelected = true;
                for (int i = 0; i < 30; i++)
                {
                    var timer = Stopwatch.StartNew();
                    queueSelected = !queueSelected;
                    form.SetQueuePagePreview(queueSelected);
                    FlushPendingPaint(form);
                    timer.Stop();
                    switchTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
                var sortedSwitchTimes = switchTimes.Order().ToArray();
                double switchP95Ms = sortedSwitchTimes[(int)Math.Ceiling(sortedSwitchTimes.Length * .95) - 1];
                success &= firstQueueSwitchMs < 200 && switchP95Ms < 100;
                File.WriteAllText(path, JsonSerializer.Serialize(new {
                    success, inputInjected = false, firstQueueSwitchMs, repeatedSwitchP95Ms = switchP95Ms,
                    repeatedSwitches = switchTimes, snapshots
                }, new JsonSerializerOptions { WriteIndented = true }));
                Environment.ExitCode = success ? 0 : 1;
            }
            catch (Exception error)
            {
                File.WriteAllText(path, error.ToString());
                Environment.ExitCode = 1;
            }
            finally { form.Close(); }
        });
        Application.Run(form);
    }
    private static Rectangle BoundsInForm(Form form, Control control) =>
        new(form.PointToClient(control.PointToScreen(Point.Empty)), control.Size);

    private static void FlushPendingPaint(Control control) =>
        _ = RedrawWindow(control.Handle, 0, 0, 0x80 | 0x100);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(nint window, nint updateRectangle, nint updateRegion, uint flags);

    private static bool FitsAncestors(Control control, Form form)
    {
        for (Control? child = control; child != null && child != form; child = child.Parent)
            if (child.Parent != null && !child.Parent.ClientRectangle.Contains(child.Bounds)) return false;
        return true;
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }
}
