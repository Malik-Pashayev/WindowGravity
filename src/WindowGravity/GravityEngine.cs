namespace WindowGravity;

/// <summary>A window taking part in the simulation. Coordinates are its visible frame, in physical pixels.</summary>
internal sealed class Body
{
    public required IntPtr Hwnd { get; init; }
    public double X, Y;
    public double Vx, Vy;
    public int Width, Height;

    // Offset from the visible frame to the raw window rect (the invisible resize borders).
    public int OffsetX, OffsetY;

    public bool Resting;
    public int StillFrames;
}

/// <summary>Tiny 2D physics: gravity, bouncing off screen edges, and stacking on other fallen windows.</summary>
internal sealed class GravityEngine
{
    private const double AirDrag = 0.15;        // fraction of speed lost per second in the air
    private const double GroundFriction = 6.0;  // how quickly sliding windows stop
    private const double MaxSpeed = 9000;       // px/s
    private const double MinBounceSpeed = 140;  // slower impacts just settle

    private readonly List<Body> _bodies = new();

    public double Gravity { get; set; } = 2600;  // px/s²
    public double Bounciness { get; set; } = 0.45;

    public void Launch(IntPtr hwnd, double vx, double vy)
    {
        Remove(hwnd);
        if (!WindowInfo.TryGetFrame(hwnd, out var frame, out var window))
            return;

        _bodies.Add(new Body
        {
            Hwnd = hwnd,
            X = frame.X,
            Y = frame.Y,
            Width = frame.Width,
            Height = frame.Height,
            OffsetX = window.X - frame.X,
            OffsetY = window.Y - frame.Y,
            Vx = Clamp(vx),
            Vy = Clamp(vy),
        });
        WakeAll();
    }

    public void Remove(IntPtr hwnd)
    {
        // Anything stacked on this window has lost its support.
        if (_bodies.RemoveAll(b => b.Hwnd == hwnd) > 0)
            WakeAll();
    }

    public void Clear() => _bodies.Clear();

    public void WakeAll()
    {
        foreach (var body in _bodies)
        {
            body.Resting = false;
            body.StillFrames = 0;
        }
    }

    public void Step(double dt)
    {
        // Closed, minimized or maximized windows leave the simulation.
        if (_bodies.RemoveAll(b => !WindowInfo.IsMovable(b.Hwnd)) > 0)
            WakeAll();

        var bounds = SystemInformation.VirtualScreen;
        foreach (var body in _bodies)
        {
            if (!body.Resting)
                StepBody(body, dt, bounds);
        }
    }

    private void StepBody(Body b, double dt, Rectangle bounds)
    {
        double drag = Math.Exp(-AirDrag * dt);
        b.Vx = Clamp(b.Vx * drag);
        b.Vy = Clamp((b.Vy + Gravity * dt) * drag);

        double x = b.X + b.Vx * dt;
        double y = b.Y + b.Vy * dt;

        // Floor and ceiling come from the monitor the window is over (taskbar excluded).
        var area = Screen.FromPoint(new Point((int)(x + b.Width / 2.0), (int)(y + b.Height / 2.0))).WorkingArea;

        double floor = area.Bottom;
        if (b.Vy >= 0)
        {
            // Land on top of another fallen window if we cross its top edge this frame.
            double previousBottom = b.Y + b.Height;
            foreach (var other in _bodies)
            {
                bool overlapsX = x < other.X + other.Width && x + b.Width > other.X;
                if (other != b && overlapsX && previousBottom <= other.Y + 1 && other.Y < floor)
                    floor = other.Y;
            }
        }

        bool grounded = false;
        if (y + b.Height >= floor)
        {
            y = floor - b.Height;
            b.Vy = b.Vy > MinBounceSpeed ? -b.Vy * Bounciness : 0;
            b.Vx *= Math.Exp(-GroundFriction * dt);
            grounded = true;
        }
        else if (y < area.Top && b.Vy < 0)
        {
            y = area.Top;
            b.Vy = -b.Vy * Bounciness;
        }

        if (x < bounds.Left)
        {
            x = bounds.Left;
            b.Vx = Math.Abs(b.Vx) * Bounciness;
        }
        else if (x + b.Width > bounds.Right)
        {
            x = bounds.Right - b.Width;
            b.Vx = -Math.Abs(b.Vx) * Bounciness;
        }

        if (grounded && b.Vy == 0 && Math.Abs(b.Vx) < 15)
        {
            if (++b.StillFrames > 10)
            {
                b.Resting = true;
                b.Vx = 0;
            }
        }
        else
        {
            b.StillFrames = 0;
        }

        MoveTo(b, x, y);
    }

    private static void MoveTo(Body b, double x, double y)
    {
        int oldX = (int)Math.Round(b.X), oldY = (int)Math.Round(b.Y);
        b.X = x;
        b.Y = y;

        int newX = (int)Math.Round(x), newY = (int)Math.Round(y);
        if (newX != oldX || newY != oldY)
            WindowInfo.Move(b.Hwnd, newX + b.OffsetX, newY + b.OffsetY);
    }

    private static double Clamp(double v) => Math.Clamp(v, -MaxSpeed, MaxSpeed);
}
