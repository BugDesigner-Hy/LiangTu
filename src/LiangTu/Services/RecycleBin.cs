using LiangTu.Native;

namespace LiangTu.Services;

public static class RecycleBin
{
    public static bool TryRecycle(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var from = path + "\0\0";
            var op = new Win32.SHFILEOPSTRUCT
            {
                wFunc = Win32.FO_DELETE,
                pFrom = from,
                fFlags = (ushort)(Win32.FOF_ALLOWUNDO | Win32.FOF_NOCONFIRMATION | Win32.FOF_SILENT)
            };
            var rc = Win32.SHFileOperation(ref op);
            if (rc != 0)
            {
                AppLog.Warn($"Recycle returned {rc} for {path}");
                return false;
            }
            AppLog.Info($"Recycled {path}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Recycle failed: {ex.Message}");
            return false;
        }
    }
}
