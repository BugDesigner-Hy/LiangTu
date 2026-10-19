using System.Diagnostics;
using System.Runtime.InteropServices;
using LiangTu.Services;

namespace LiangTu.Native;

/// <summary>Open a file the same way Explorer double-click does (Shell / default association).</summary>
internal static class ShellFile
{
    private const int SW_SHOWNORMAL = 1;
    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const uint SEE_MASK_NOASYNC = 0x00000100;
    private const uint SEE_MASK_CLASSNAME = 0x00000001;

    /// <summary>
    /// Mirrors Explorer: default association → ShellExecuteEx → InvokeVerb → explorer → Paint.
    /// Tipray-trusted Shell paths succeed where Process.Start(file) fails on encrypted BMP.
    /// </summary>
    public static bool TryOpenLikeExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

        // 1) Empty verb = whatever Windows has as the default for this extension.
        if (TryShellExecuteEx(path, verb: null)) return true;
        if (TryShellExecuteEx(path, verb: "open")) return true;
        if (TryInvokeVerb(path, "open")) return true;
        // 2) Let Explorer resolve the association (often Tipray-friendly).
        if (TryExplorerOpen(path)) return true;
        if (TryFileProtocolHandler(path)) return true;
        // 3) Force common image viewers via ShellExecute (still goes through Shell).
        if (TryShellOpenWithApp("mspaint.exe", path)) return true;
        if (TryShellOpenWithApp(@"%SystemRoot%\System32\rundll32.exe",
                $"\"%ProgramFiles%\\Windows Photo Viewer\\PhotoViewer.dll\", ImageView_Fullscreen {path}"))
            return true;
        return false;
    }

    /// <summary>Open with Paint via Shell (for markup).</summary>
    public static bool TryOpenWithPaint(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        return TryShellOpenWithApp("mspaint.exe", path)
               || TryInvokeVerb(path, "edit");
    }

    private static bool TryShellExecuteEx(string path, string? verb)
    {
        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = SEE_MASK_INVOKEIDLIST | SEE_MASK_NOASYNC,
                hwnd = IntPtr.Zero,
                lpVerb = verb,
                lpFile = path,
                lpDirectory = Path.GetDirectoryName(path),
                nShow = SW_SHOWNORMAL
            };
            if (!ShellExecuteEx(ref info))
            {
                AppLog.Warn($"ShellExecuteEx(verb={verb ?? "default"}) failed: {Marshal.GetLastWin32Error()}");
                return false;
            }
            AppLog.Info($"ShellExecuteEx ok verb={verb ?? "default"} → {path}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"ShellExecuteEx: {ex.Message}");
            return false;
        }
    }

    private static bool TryShellOpenWithApp(string app, string path)
    {
        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = SEE_MASK_NOASYNC,
                hwnd = IntPtr.Zero,
                lpVerb = "open",
                lpFile = Environment.ExpandEnvironmentVariables(app),
                lpParameters = $"\"{path}\"",
                lpDirectory = Path.GetDirectoryName(path),
                nShow = SW_SHOWNORMAL
            };
            if (!ShellExecuteEx(ref info))
            {
                AppLog.Warn($"ShellOpenWithApp {app} failed: {Marshal.GetLastWin32Error()}");
                return false;
            }
            AppLog.Info($"ShellOpenWithApp {app} → {path}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"ShellOpenWithApp {app}: {ex.Message}");
            return false;
        }
    }

    private static bool TryExplorerOpen(string path)
    {
        try
        {
            // explorer.exe "file" uses the same association path as double-click in many Tipray setups.
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
            AppLog.Info($"explorer open → {path}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"explorer open: {ex.Message}");
            return false;
        }
    }

    private static bool TryInvokeVerb(string path, string verb)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return false;
            var shell = Activator.CreateInstance(type);
            if (shell is null) return false;
            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return false;

            var folder = type.InvokeMember(
                "NameSpace",
                System.Reflection.BindingFlags.InvokeMethod,
                null, shell, [dir]);
            if (folder is null) return false;

            var folderType = folder.GetType();
            var item = folderType.InvokeMember(
                "ParseName",
                System.Reflection.BindingFlags.InvokeMethod,
                null, folder, [name]);
            if (item is null) return false;

            item.GetType().InvokeMember(
                "InvokeVerb",
                System.Reflection.BindingFlags.InvokeMethod,
                null, item, [verb]);
            AppLog.Info($"InvokeVerb({verb}) → {path}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"InvokeVerb {verb}: {ex.Message}");
            return false;
        }
    }

    private static bool TryFileProtocolHandler(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = $"url.dll,FileProtocolHandler \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"FileProtocolHandler: {ex.Message}");
            return false;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }
}
