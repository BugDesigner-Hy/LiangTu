using LiangTu.Native;

namespace LiangTu.Services;

internal static class DpiHelper
{
    /// <summary>
    /// Convert a physical-pixel monitor rect from GetMonitorInfo into WPF DIPs.
    /// </summary>
    public static WpfRect PhysicalMonitorToDip(IntPtr hMonitor, Win32.RECT physical)
    {
        var scaleX = 1.0;
        var scaleY = 1.0;
        try
        {
            if (Win32.GetDpiForMonitor(hMonitor, Win32.MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0)
            {
                scaleX = dpiX / 96.0;
                scaleY = dpiY / 96.0;
            }
        }
        catch
        {
            // Shcore missing on very old OS — fall back to system DPI
            try
            {
                using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
                scaleX = g.DpiX / 96.0;
                scaleY = g.DpiY / 96.0;
            }
            catch { /* keep 1.0 */ }
        }

        if (scaleX < 0.5) scaleX = 1.0;
        if (scaleY < 0.5) scaleY = 1.0;

        return new WpfRect(
            physical.Left / scaleX,
            physical.Top / scaleY,
            physical.Width / scaleX,
            physical.Height / scaleY);
    }
}
