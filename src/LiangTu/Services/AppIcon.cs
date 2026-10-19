using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LiangTu.Services;

internal static class AppIcon
{
    private static Icon? _trayIcon;

    public static Icon GetTrayIcon()
    {
        if (_trayIcon is not null) return _trayIcon;

        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
            if (File.Exists(icoPath))
            {
                // Clone so file handle is not kept locked.
                using var tmp = new Icon(icoPath, 32, 32);
                _trayIcon = (Icon)tmp.Clone();
                return _trayIcon;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Load tray ico failed: {ex.Message}");
        }

        try
        {
            var uri = new Uri("pack://application:,,,/Assets/logo.png", UriKind.Absolute);
            var bmp = new BitmapImage(uri);
            bmp.Freeze();
            _trayIcon = BitmapSourceToIcon(bmp, 32);
            if (_trayIcon is not null) return _trayIcon;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Load tray png failed: {ex.Message}");
        }

        _trayIcon = SystemIcons.Application;
        return _trayIcon;
    }

    public static void ApplyToWindow(Window window)
    {
        try
        {
            window.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/logo.png", UriKind.Absolute));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Apply window icon failed: {ex.Message}");
        }
    }

    private static Icon? BitmapSourceToIcon(BitmapSource source, int size)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        using var img = System.Drawing.Image.FromStream(ms);
        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, 0, 0, size, size);
        }
        var hIcon = bmp.GetHicon();
        using var icon = Icon.FromHandle(hIcon);
        return (Icon)icon.Clone();
    }
}
