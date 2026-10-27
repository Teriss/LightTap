using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KeyKing;

internal static class Native
{
    internal const int HotKeyMessage = 0x0312;
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardHookData
    {
        public uint VirtualKey, ScanCode, Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseHookData
    {
        public Point Point;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort Key, Scan;
        public uint Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;
        public InputUnion Data;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint window);
    internal delegate nint KeyboardHookCallback(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint SetWindowsHookEx(int type, KeyboardHookCallback callback, nint module, uint thread);
    [DllImport("user32.dll")]
    internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? module);
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);
    internal static Point GetCursorPosition()
    {
        if (!GetCursorPos(out var point)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取鼠标位置。");
        return new(point.X, point.Y);
    }
    internal static void MoveCursor(Point point)
    {
        if (!SetCursorPos(point.X, point.Y)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法移动鼠标。");
    }

    internal static void Send(ClickOptions options, bool down)
    {
        Input input;
        if (options.Mode == ClickMode.Keyboard)
        {
            uint flags = down ? 0u : 2u;
            if (options.Key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Insert
                or Keys.Delete or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.Divide)
                flags |= 1;
            input = new Input { Type = 1, Data = new InputUnion {
                Keyboard = new KeyboardInput { Key = (ushort)options.Key,
                    Scan = (ushort)MapVirtualKey((uint)options.Key, 0), Flags = flags } } };
        }
        else
        {
            uint flag = options.Mode switch
            {
                ClickMode.LeftMouse => down ? 0x0002u : 0x0004u,
                ClickMode.RightMouse => down ? 0x0008u : 0x0010u,
                _ => down ? 0x0020u : 0x0040u
            };
            input = new Input { Type = 0, Data = new InputUnion {
                Mouse = new MouseInput { Flags = flag } } };
        }
        SendOne(input);
    }

    internal static void SendRecorded(RecordedInput action)
    {
        Input input;
        if (action.Kind is RecordedInputKind.KeyDown or RecordedInputKind.KeyUp)
        {
            uint flags = action.Kind == RecordedInputKind.KeyUp ? 2u : 0u;
            if (action.Extended) flags |= 1;
            input = new Input { Type = 1, Data = new InputUnion {
                Keyboard = new KeyboardInput { Key = (ushort)action.Code,
                    Scan = (ushort)action.ScanCode, Flags = flags } } };
        }
        else if (action.Kind is RecordedInputKind.MouseButtonDown or RecordedInputKind.MouseButtonUp)
        {
            bool down = action.Kind == RecordedInputKind.MouseButtonDown;
            var button = (RecordedMouseButton)action.Code;
            uint flags = button switch
            {
                RecordedMouseButton.Left => down ? 0x0002u : 0x0004u,
                RecordedMouseButton.Right => down ? 0x0008u : 0x0010u,
                RecordedMouseButton.Middle => down ? 0x0020u : 0x0040u,
                RecordedMouseButton.X1 or RecordedMouseButton.X2 => down ? 0x0080u : 0x0100u,
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
            uint data = button == RecordedMouseButton.X1 ? 1u : button == RecordedMouseButton.X2 ? 2u : 0u;
            input = new Input { Type = 0, Data = new InputUnion {
                Mouse = new MouseInput { Data = data, Flags = flags } } };
        }
        else if (action.Kind == RecordedInputKind.MouseWheel)
        {
            input = new Input { Type = 0, Data = new InputUnion {
                Mouse = new MouseInput { Data = unchecked((uint)(action.Delta << 16)), Flags = 0x0800u } } };
        }
        else throw new ArgumentOutOfRangeException(nameof(action));
        SendOne(input);
    }

    private static void SendOne(Input input)
    {
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "输入发送失败。请检查目标窗口权限；普通权限程序可能无法操作管理员窗口。");
    }
}
