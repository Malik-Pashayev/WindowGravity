using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace WindowGravity;

/// <summary>The app itself: a tray icon, a 100 Hz physics loop and a few hotkeys.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private static readonly (string Name, double Value)[] GravityPresets =
    [
        ("Moon", 450), ("Earth", 2600), ("Jupiter", 6500), ("Zero-G", 0),
    ];

    private static readonly (string Name, double Value)[] BouncinessPresets =
    [
        ("Brick", 0.1), ("Rubber", 0.45), ("Super ball", 0.85),
    ];

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly GravityEngine _engine = new();
    private readonly DragTracker _drag;
    private readonly HotkeyWindow _hotkeys = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10 };
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly Dictionary<IntPtr, Point> _positionsBeforeRain = new();
    private readonly Random _random = new();
    private double _lastTick;
    private bool _enabled = true;

    public TrayApp()
    {
        Native.timeBeginPeriod(1); // smoother timer ticks

        _drag = new DragTracker(Now);
        _drag.DragStarted += hwnd => _engine.Remove(hwnd);
        _drag.Thrown += (hwnd, vx, vy) =>
        {
            if (_enabled)
                _engine.Launch(hwnd, vx, vy);
        };

        _enabledItem = new ToolStripMenuItem("Gravity on", null, (_, _) => ToggleEnabled())
        {
            Checked = true,
            ShortcutKeyDisplayString = "Ctrl+Alt+G",
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new ToolStripMenuItem("Make it rain", null, (_, _) => MakeItRain()) { ShortcutKeyDisplayString = "Ctrl+Alt+R" });
        menu.Items.Add(new ToolStripMenuItem("Put windows back", null, (_, _) => PutWindowsBack()) { ShortcutKeyDisplayString = "Ctrl+Alt+Z" });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(ChoiceMenu("Gravity", GravityPresets, _engine.Gravity, v => { _engine.Gravity = v; _engine.WakeAll(); }));
        menu.Items.Add(ChoiceMenu("Bounciness", BouncinessPresets, _engine.Bounciness, v => _engine.Bounciness = v));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));

        _tray = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "Window Gravity",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ToggleEnabled();

        _hotkeys.Register(Keys.G, ToggleEnabled);
        _hotkeys.Register(Keys.R, MakeItRain);
        _hotkeys.Register(Keys.Z, PutWindowsBack);

        _lastTick = Now();
        _timer.Tick += OnTick;
        _timer.Start();

        _tray.ShowBalloonTip(4000, "Window Gravity is on",
            "Grab any window by its title bar, fling it and let go.", ToolTipIcon.None);
    }

    private double Now() => _clock.Elapsed.TotalSeconds;

    private void OnTick(object? sender, EventArgs e)
    {
        double now = Now();
        double dt = Math.Min(now - _lastTick, 0.05); // don't explode after a stall
        _lastTick = now;

        _drag.Sample();
        if (_enabled)
            _engine.Step(dt);
    }

    private void ToggleEnabled()
    {
        _enabled = !_enabled;
        _enabledItem.Checked = _enabled;
        _tray.Text = _enabled ? "Window Gravity" : "Window Gravity (paused)";
        if (!_enabled)
            _engine.Clear();
    }

    /// <summary>Drops every open window at once, with a random little toss.</summary>
    private void MakeItRain()
    {
        if (!_enabled)
            ToggleEnabled();

        foreach (var hwnd in WindowInfo.GetThrowableWindows())
        {
            if (Native.GetWindowRect(hwnd, out var rect))
                _positionsBeforeRain.TryAdd(hwnd, new Point(rect.Left, rect.Top));

            _engine.Launch(hwnd, _random.Next(-900, 900), _random.Next(-1200, -200));
        }
    }

    private void PutWindowsBack()
    {
        _engine.Clear();
        foreach (var (hwnd, position) in _positionsBeforeRain)
        {
            if (WindowInfo.IsMovable(hwnd))
                WindowInfo.Move(hwnd, position.X, position.Y);
        }
        _positionsBeforeRain.Clear();
    }

    private static ToolStripMenuItem ChoiceMenu(string title, (string Name, double Value)[] choices,
        double current, Action<double> apply)
    {
        var parent = new ToolStripMenuItem(title);
        foreach (var (name, value) in choices)
        {
            var item = new ToolStripMenuItem(name) { Checked = value == current };
            item.Click += (_, _) =>
            {
                foreach (ToolStripMenuItem sibling in parent.DropDownItems)
                    sibling.Checked = sibling == item;
                apply(value);
            };
            parent.DropDownItems.Add(item);
        }
        return parent;
    }

    /// <summary>Draws the tray icon at runtime: a window falling onto the ground.</summary>
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var window = new SolidBrush(Color.FromArgb(96, 165, 250));
            using var titleBar = new SolidBrush(Color.FromArgb(29, 78, 216));
            using var arrow = new SolidBrush(Color.FromArgb(249, 115, 22));
            using var ground = new SolidBrush(Color.FromArgb(148, 163, 184));

            g.FillRectangle(window, 5, 2, 22, 15);
            g.FillRectangle(titleBar, 5, 2, 22, 4);
            g.FillPolygon(arrow, [new Point(16, 28), new Point(9, 20), new Point(23, 20)]);
            g.FillRectangle(ground, 1, 29, 30, 3);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _drag.Dispose();
        _hotkeys.Dispose();
        Native.timeEndPeriod(1);
        base.ExitThreadCore();
    }
}
