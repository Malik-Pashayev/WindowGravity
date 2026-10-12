namespace WindowGravity;

/// <summary>Hidden message-only window that receives global Ctrl+Alt hotkeys.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private readonly Dictionary<int, Action> _actions = new();

    public HotkeyWindow() => CreateHandle(new CreateParams { Parent = HWND_MESSAGE });

    /// <summary>Registers Ctrl+Alt+<paramref name="key"/>. Returns false if another app already owns it.</summary>
    public bool Register(Keys key, Action action)
    {
        int id = _actions.Count + 1;
        if (!Native.RegisterHotKey(Handle, id, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)key))
            return false;

        _actions[id] = action;
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var action))
            action();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (int id in _actions.Keys)
            Native.UnregisterHotKey(Handle, id);
        DestroyHandle();
    }
}
