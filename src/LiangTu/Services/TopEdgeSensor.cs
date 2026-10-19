using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using LiangTu.Native;

namespace LiangTu.Services;

public sealed class TopEdgeSensor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private DateTime? _hotSince;
    private DateTime? _awaySince;
    private bool _enabled;

    public double RevealDelaySeconds { get; set; } = 0.25;
    public double RetractDelaySeconds { get; set; } = 0.5;
    public int HotZoneHeightDip { get; set; } = 8;

    public event Action<WpfRect>? ShouldReveal;
    public event Action<WpfRect>? ShouldMoveToMonitor;
    public event Action? ShouldRetract;
    public Func<bool>? IsBusy;
    public Func<bool>? IsRevealed;
    public Func<WpfRect>? GetLineZone;
    public Func<WpfRect>? GetLineMonitor;

    public TopEdgeSensor()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
        _timer.Tick += (_, _) => Tick();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled) _timer.Start();
        else
        {
            _timer.Stop();
            _hotSince = null;
            _awaySince = null;
        }
    }

    private void Tick()
    {
        if (!_enabled) return;
        if (!Win32.GetCursorPos(out var pt)) return;

        var monitor = GetMonitorUnderPoint(pt);
        var mouseDip = PhysicalPointToDip(pt, monitor);
        var revealed = IsRevealed?.Invoke() ?? false;
        var inHot = mouseDip.Y >= monitor.Top && mouseDip.Y <= monitor.Top + HotZoneHeightDip
                    && mouseDip.X >= monitor.Left && mouseDip.X <= monitor.Right;

        if (!revealed)
        {
            if (inHot)
            {
                _hotSince ??= DateTime.Now;
                if ((DateTime.Now - _hotSince.Value).TotalSeconds >= RevealDelaySeconds)
                {
                    _hotSince = null;
                    ShouldReveal?.Invoke(monitor);
                }
            }
            else _hotSince = null;
            return;
        }

        // Already revealed: resting on another screen's top edge moves the rope there.
        var lineMon = GetLineMonitor?.Invoke() ?? GetLineZone?.Invoke() ?? monitor;
        if (inHot && !SameMonitor(monitor, lineMon))
        {
            _hotSince ??= DateTime.Now;
            if ((DateTime.Now - _hotSince.Value).TotalSeconds >= RevealDelaySeconds)
            {
                _hotSince = null;
                _awaySince = null;
                ShouldMoveToMonitor?.Invoke(monitor);
            }
            return;
        }

        if (!inHot) _hotSince = null;

        var zone = GetLineZone?.Invoke() ?? lineMon;
        zone = new WpfRect(zone.X, lineMon.Top, zone.Width, Math.Max(1, zone.Bottom - lineMon.Top));
        var inside = zone.Contains(mouseDip);
        var busy = IsBusy?.Invoke() ?? false;

        if (inside || busy)
        {
            _awaySince = null;
        }
        else
        {
            _awaySince ??= DateTime.Now;
            if ((DateTime.Now - _awaySince.Value).TotalSeconds >= RetractDelaySeconds)
            {
                _awaySince = null;
                ShouldRetract?.Invoke();
            }
        }
    }

    public static bool SameMonitor(WpfRect a, WpfRect b, double tol = 12) =>
        Math.Abs(a.Left - b.Left) < tol
        && Math.Abs(a.Top - b.Top) < tol
        && Math.Abs(a.Width - b.Width) < tol;

    public static WpfRect GetMonitorUnderCursor()
    {
        Win32.GetCursorPos(out var pt);
        return GetMonitorUnderPoint(pt);
    }

    public static List<MonitorInfo> GetAllMonitors()
    {
        var list = new List<(IntPtr h, Win32.RECT r, bool primary)>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref Win32.RECT __, IntPtr ___) =>
        {
            var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
            if (Win32.GetMonitorInfo(hMon, ref info))
                list.Add((hMon, info.rcMonitor, (info.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);

        return list
            .OrderByDescending(x => x.primary)
            .ThenBy(x => x.r.Left)
            .ThenBy(x => x.r.Top)
            .Select((x, i) => new MonitorInfo
            {
                Index = i,
                Bounds = DpiHelper.PhysicalMonitorToDip(x.h, x.r),
                IsPrimary = x.primary
            })
            .ToList();
    }

    /// <summary>Find the closest current monitor matching a previously saved DIP origin/size.</summary>
    public static WpfRect ResolvePreferredMonitor(double left, double top, double width)
    {
        var all = GetAllMonitors();
        if (all.Count == 0) return GetMonitorUnderCursor();

        var match = all.FirstOrDefault(m =>
            Math.Abs(m.Bounds.Left - left) < 40 && Math.Abs(m.Bounds.Top - top) < 40);
        if (match is not null) return match.Bounds;

        // Fallback: nearest by center distance
        var cx = left + width / 2;
        return all
            .OrderBy(m => Math.Abs(m.Bounds.Left + m.Bounds.Width / 2 - cx))
            .First()
            .Bounds;
    }

    internal static WpfRect GetMonitorUnderPoint(Win32.POINT pt)
    {
        var hMon = Win32.MonitorFromPoint(pt, 2); // MONITOR_DEFAULTTONEAREST
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (Win32.GetMonitorInfo(hMon, ref info))
            return DpiHelper.PhysicalMonitorToDip(hMon, info.rcMonitor);

        return new WpfRect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    internal static WpfPoint PhysicalPointToDip(Win32.POINT pt, WpfRect monitorDip)
    {
        var hMon = Win32.MonitorFromPoint(pt, 2);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(hMon, ref info) || monitorDip.Width < 1 || info.rcMonitor.Width < 1)
            return new WpfPoint(pt.X, pt.Y);

        var scaleX = info.rcMonitor.Width / monitorDip.Width;
        var scaleY = info.rcMonitor.Height / monitorDip.Height;
        return new WpfPoint(
            monitorDip.Left + (pt.X - info.rcMonitor.Left) / scaleX,
            monitorDip.Top + (pt.Y - info.rcMonitor.Top) / scaleY);
    }

    public void Dispose() => _timer.Stop();
}
