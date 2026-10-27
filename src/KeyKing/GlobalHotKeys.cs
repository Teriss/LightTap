using System.Runtime.InteropServices;

namespace KeyKing;

internal sealed record HotKeyBinding(Keys Key, uint Modifiers = 0)
{
    public override string ToString()
    {
        string prefix = ((Modifiers & 2) != 0 ? "Ctrl+" : "") + ((Modifiers & 1) != 0 ? "Alt+" : "")
            + ((Modifiers & 4) != 0 ? "Shift+" : "");
        string key = Key switch {
            >= Keys.D0 and <= Keys.D9 => ((char)Key).ToString(),
            Keys.Space => "Space", Keys.Return => "Enter", Keys.Escape => "Esc", Keys.Back => "Backspace",
            Keys.Next => "PageDown", Keys.Prior => "PageUp", _ => Key.ToString() };
        return prefix + key;
    }
}

internal static class GlobalHotKeys
{
    internal static readonly string[] Names = ["F8", "Ctrl+Alt+F8", "Ctrl+Shift+F8"];
    internal static uint Modifiers(int scheme) => scheme switch { 1 => 3u, 2 => 6u, _ => 0u };
    internal static HotKeyBinding Preset(int scheme) => new(Keys.F8, Modifiers(scheme));
    internal static string? Validate(HotKeyBinding key)
    {
        if ((key.Modifiers & 8) != 0) return "快捷键支持 Ctrl、Alt、Shift 组合，暂不支持 Windows 键。";
        if ((key.Modifiers & ~7u) != 0 || (int)key.Key < 8 || (int)key.Key > 254
            || key.Key is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LShiftKey or Keys.RShiftKey
                or Keys.LControlKey or Keys.RControlKey or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin
                or Keys.ProcessKey or Keys.Packet)
            return "请设置一个按键，可搭配 Ctrl、Alt、Shift；不能只设置修饰键。";
        if (key.Key == Keys.F12) return "F12 为 Windows 调试器保留键，请选择其他快捷键。";
        return null;
    }
    internal static string? InputConflict(ClickOptions options) =>
        options.Mode == ClickMode.Keyboard && options.ToggleBinding.Key == options.Key && options.ToggleBinding.Modifiers == 0
        ? "连点按键不能与无修饰键的启停快捷键相同。" : null;
    internal static void Unregister(nint window, int id = 1)
    {
        Native.UnregisterHotKey(window, id);
    }
    internal static bool RegisterSingle(nint window, HotKeyBinding binding, int id, out string message)
    {
        if (Validate(binding) is string invalid) { message = invalid; return false; }
        Unregister(window, id);
        if (!Native.RegisterHotKey(window, id, 0x4000 | binding.Modifiers, (uint)binding.Key))
        { message = Failure(binding); return false; }
        message = "快捷键可用，已生效。";
        return true;
    }
    private static string Failure(HotKeyBinding key)
    {
        int error = Marshal.GetLastWin32Error();
        return error == 1409 ? $"快捷键 {key} 已被占用，请更换。"
            : $"快捷键 {key} 注册失败（错误 {error}），请更换。";
    }
}
