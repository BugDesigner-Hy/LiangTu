using Microsoft.Win32;

namespace LiangTu.Services;

/// <summary>Cleans up the legacy Explorer「一键晾图」verb if it was registered previously.</summary>
public static class ShellContextMenu
{
    private const string VerbName = "LiangTuHang";

    private static readonly string[] MenuRoots =
    [
        @"Software\Classes\SystemFileAssociations\image\shell\" + VerbName,
        @"Software\Classes\SystemFileAssociations\.png\shell\" + VerbName,
        @"Software\Classes\SystemFileAssociations\.jpg\shell\" + VerbName,
        @"Software\Classes\SystemFileAssociations\.jpeg\shell\" + VerbName,
        @"Software\Classes\SystemFileAssociations\.bmp\shell\" + VerbName,
        @"Software\Classes\SystemFileAssociations\.gif\shell\" + VerbName,
        @"Software\Classes\SystemFileAssociations\.webp\shell\" + VerbName,
        @"Software\Classes\SystemFileAssociations\.tif\shell\" + VerbName,
        @"Software\Classes\SystemFileAssociations\.tiff\shell\" + VerbName,
        @"Software\Classes\*\shell\" + VerbName,
    ];

    public static void UninstallIfPresent()
    {
        var removed = false;
        foreach (var root in MenuRoots)
        {
            try
            {
                using var existing = Registry.CurrentUser.OpenSubKey(root);
                if (existing is null) continue;
                Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
                removed = true;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Uninstall shell key {root}: {ex.Message}");
            }
        }

        if (!removed) return;

        AppLog.Info("Legacy Explorer「一键晾图」menu removed");
        try { Native.Win32.SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero); }
        catch { /* ignore */ }
    }
}
