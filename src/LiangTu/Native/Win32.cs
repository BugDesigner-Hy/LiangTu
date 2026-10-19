using System.Runtime.InteropServices;
using System.Text;

namespace LiangTu.Native;

internal static class Win32
{
    public const uint WM_GETTEXT = 0x000D;
    public const uint WM_GETTEXTLENGTH = 0x000E;
    public const uint WM_SETTEXT = 0x000C;
    public const uint WM_COMMAND = 0x0111;
    public const uint BM_CLICK = 0x00F5;
    public const uint CB_GETCOUNT = 0x0146;
    public const uint CB_GETLBTEXT = 0x0148;
    public const uint CB_GETLBTEXTLEN = 0x0149;
    public const uint CB_SETCURSEL = 0x014E;
    public const uint CB_FINDSTRINGEXACT = 0x0158;
    public const uint CB_FINDSTRING = 0x014C;
    public const uint CBN_SELENDOK = 9;
    public const int edt1 = 0x0480;
    public const int cmb1 = 0x0470;   // Save-as type filter
    public const int cmb13 = 0x047C;  // File name
    public const int IDOK = 1;

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_LAYERED = 0x00080000;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;

    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumProc cb, IntPtr lp);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr lp);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr h);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr h, StringBuilder s, int n);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr h);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr h, out uint id);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr h, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr h, uint Msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr h, uint Msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    public const uint MONITORINFOF_PRIMARY = 1;

    public const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("Shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12; // Alt
    public const ushort VK_P = 0x50;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    /// <summary>Must include MOUSEINPUT so sizeof(INPUT) matches Win32 (40 on x64).</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    /// <summary>SHCNE_ASSOCCHANGED = 0x08000000, SHCNF_IDLIST = 0x0000 / SHCNF_FLUSH = 0x1000</summary>
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public const ushort FO_DELETE = 0x0003;
    public const ushort FOF_ALLOWUNDO = 0x0040;
    public const ushort FOF_NOCONFIRMATION = 0x0010;
    public const ushort FOF_SILENT = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public ushort wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }
}
