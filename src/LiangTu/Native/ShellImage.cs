using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace LiangTu.Native;

/// <summary>
/// Loads a preview bitmap through the Windows Shell (Explorer path).
/// Tipray/蓝盾 often returns ciphertext to direct File.Read by untrusted apps,
/// while IShellItemImageFactory still yields a viewable HBITMAP.
/// </summary>
internal static class ShellImage
{
    private const int SIIGBF_RESIZETOFIT = 0x00;
    private const int SIIGBF_BIGGERSIZEOK = 0x01;
    private const int SIIGBF_THUMBNAILONLY = 0x00000008;
    private const int SIIGBF_QUALITYONLY = 0x00000010;

    public static BitmapSource? TryGetBitmap(string path, int maxPixels)
    {
        IntPtr hbm = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            if (hr != 0 || factory is null) return null;

            var size = new SIZE { cx = maxPixels, cy = maxPixels };
            // Prefer full-quality image; fall back to thumbnail-only.
            hr = factory.GetImage(size, SIIGBF_RESIZETOFIT | SIIGBF_BIGGERSIZEOK | SIIGBF_QUALITYONLY, out hbm);
            if (hr != 0 || hbm == IntPtr.Zero)
            {
                hr = factory.GetImage(size, SIIGBF_RESIZETOFIT | SIIGBF_BIGGERSIZEOK | SIIGBF_THUMBNAILONLY, out hbm);
                if (hr != 0 || hbm == IntPtr.Zero) return null;
            }

            var src = Imaging.CreateBitmapSourceFromHBitmap(
                hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }
}
