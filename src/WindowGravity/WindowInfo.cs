using System.Runtime.InteropServices;
using System.Text;

namespace WindowGravity;

/// <summary>Helpers for reading, filtering and moving top-level windows.</summary>
internal static class WindowInfo
{
    // Desktop, taskbar and other shell surfaces must never fall.
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
    };

    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    /// <summary>
    /// Gets the visible frame (without the invisible resize borders Windows 10/11 add)
    /// and the raw window rectangle that SetWindowPos works with.
    /// </summary>
    public static bool TryGetFrame(IntPtr hwnd, out Rectangle frame, out Rectangle window)
    {
        frame = window = Rectangle.Empty;
        if (!Native.GetWindowRect(hwnd, out var raw))
            return false;

        window = raw.ToRectangle();
        frame = Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS,
            out Native.RECT visible, Marshal.SizeOf<Native.RECT>()) == 0
            ? visible.ToRectangle()
            : window;

        return frame.Width > 0 && frame.Height > 0;
    }

    /// <summary>A window we may keep simulating: alive, shown, not minimized or maximized.</summary>
    public static bool IsMovable(IntPtr hwnd) =>
        Native.IsWindow(hwnd) && Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd) && !Native.IsZoomed(hwnd);

    /// <summary>A normal application window that is fair game for gravity.</summary>
    public static bool IsThrowable(IntPtr hwnd)
    {
        if (!IsMovable(hwnd) || Native.GetAncestor(hwnd, Native.GA_ROOT) != hwnd)
            return false;

        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == OwnProcessId)
            return false;

        long style = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
        long exStyle = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        if ((style & Native.WS_CHILD) != 0 || (exStyle & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE)) != 0)
            return false;

        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;

        var className = new StringBuilder(256);
        Native.GetClassName(hwnd, className, className.Capacity);
        return !ShellClasses.Contains(className.ToString());
    }

    /// <summary>All visible, titled application windows, front to back.</summary>
    public static List<IntPtr> GetThrowableWindows()
    {
        var result = new List<IntPtr>();
        Native.EnumWindows((hwnd, lParam) =>
        {
            if (Native.GetWindowTextLength(hwnd) > 0 && IsThrowable(hwnd) &&
                TryGetFrame(hwnd, out var frame, out _) && frame.Width >= 120 && frame.Height >= 80)
            {
                result.Add(hwnd);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static void Move(IntPtr hwnd, int x, int y) =>
        Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE |
            Native.SWP_NOOWNERZORDER | Native.SWP_ASYNCWINDOWPOS);
}
