namespace WindowGravity;

/// <summary>
/// Watches the user dragging windows (system-wide) and reports the release velocity,
/// so a window can be "thrown" when the mouse button is let go.
/// </summary>
internal sealed class DragTracker : IDisposable
{
    private const double SampleWindowSeconds = 0.25;
    private const double VelocityWindowSeconds = 0.08;

    private readonly Func<double> _clock;
    private readonly Native.WinEventDelegate _callback; // held in a field so the GC can't collect it
    private readonly IntPtr _hook;
    private readonly List<(double Time, Rectangle Frame)> _samples = new();
    private IntPtr _dragged;

    public event Action<IntPtr>? DragStarted;
    public event Action<IntPtr, double, double>? Thrown;

    public DragTracker(Func<double> clock)
    {
        _clock = clock;
        _callback = OnWinEvent;
        _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_MOVESIZESTART, Native.EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, _callback, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
    }

    /// <summary>Records the dragged window's position; call this every frame.</summary>
    public void Sample()
    {
        if (_dragged == IntPtr.Zero || !WindowInfo.TryGetFrame(_dragged, out var frame, out _))
            return;

        double now = _clock();
        _samples.Add((now, frame));
        _samples.RemoveAll(s => now - s.Time > SampleWindowSeconds);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != Native.OBJID_WINDOW || idChild != Native.CHILDID_SELF)
            return;

        if (eventType == Native.EVENT_SYSTEM_MOVESIZESTART)
        {
            _samples.Clear();
            _dragged = WindowInfo.IsThrowable(hwnd) ? hwnd : IntPtr.Zero;
            if (_dragged == IntPtr.Zero)
                return;

            Sample();
            DragStarted?.Invoke(hwnd);
        }
        else if (eventType == Native.EVENT_SYSTEM_MOVESIZEEND && hwnd == _dragged)
        {
            _dragged = IntPtr.Zero;
            if (TryGetReleaseVelocity(hwnd, out double vx, out double vy))
                Thrown?.Invoke(hwnd, vx, vy);
        }
    }

    private bool TryGetReleaseVelocity(IntPtr hwnd, out double vx, out double vy)
    {
        vx = vy = 0;
        if (_samples.Count == 0 || !WindowInfo.IsMovable(hwnd) || !WindowInfo.TryGetFrame(hwnd, out var final, out _))
            return false;

        // A resize, or Aero Snap tiling the window, means this wasn't a throw.
        var last = _samples[^1].Frame;
        if (final.Size != last.Size || Math.Abs(final.X - last.X) > 60 || Math.Abs(final.Y - last.Y) > 60)
            return false;

        double now = _clock();
        foreach (var (time, frame) in _samples)
        {
            double span = now - time;
            if (span > VelocityWindowSeconds)
                continue;

            // Oldest sample inside the window; if it's too recent the user was holding still.
            if (span > 0.01)
            {
                vx = (final.X - frame.X) / span;
                vy = (final.Y - frame.Y) / span;
            }
            break;
        }
        return true;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            Native.UnhookWinEvent(_hook);
    }
}
