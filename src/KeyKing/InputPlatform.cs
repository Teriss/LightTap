namespace KeyKing;

internal interface IInputPlatform
{
    nint ForegroundWindow { get; }
    bool IsPressed(int key);
    Point CursorPosition { get; }
    Rectangle ScreenBounds(Point point);
    void MoveCursor(Point point);
    void Send(ClickOptions options, bool down);
}

internal sealed class WindowsInputPlatform : IInputPlatform
{
    public nint ForegroundWindow => Native.GetForegroundWindow();
    public bool IsPressed(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
    public Point CursorPosition => Native.GetCursorPosition();
    public Rectangle ScreenBounds(Point point) => Screen.FromPoint(point).Bounds;
    public void MoveCursor(Point point) => Native.MoveCursor(point);
    public void Send(ClickOptions options, bool down) => Native.Send(options, down);
}

internal static class MouseOffset
{
    internal static Point Clamp(Point center, int dx, int dy, Rectangle screen) => new(
        (int)Math.Clamp((long)center.X + dx, screen.Left, (long)screen.Right - 1),
        (int)Math.Clamp((long)center.Y + dy, screen.Top, (long)screen.Bottom - 1));
}
