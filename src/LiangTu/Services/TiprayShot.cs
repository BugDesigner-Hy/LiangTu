using System.Runtime.InteropServices;
using LiangTu.Native;

namespace LiangTu.Services;

/// <summary>Simulate Tipray's fixed screenshot chord Ctrl+Alt+P.</summary>
public static class TiprayShot
{
    private static DateTime _lastSent = DateTime.MinValue;
    private static readonly object Gate = new();

    public static bool SendCtrlAltP()
    {
        lock (Gate)
        {
            if ((DateTime.UtcNow - _lastSent).TotalMilliseconds < 400)
                return false;
            _lastSent = DateTime.UtcNow;
        }

        try
        {
            if (SendViaSendInput())
            {
                AppLog.Info("TiprayShot: SendInput Ctrl+Alt+P");
                return true;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"TiprayShot SendInput failed: {ex.Message}");
        }

        try
        {
            System.Windows.Forms.SendKeys.SendWait("^%p");
            AppLog.Info("TiprayShot: SendKeys Ctrl+Alt+P");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"TiprayShot SendKeys failed: {ex.Message}");
            return false;
        }
    }

    private static bool SendViaSendInput()
    {
        var inputs = new Win32.INPUT[]
        {
            Key(Win32.VK_CONTROL, down: true),
            Key(Win32.VK_MENU, down: true),
            Key(Win32.VK_P, down: true),
            Key(Win32.VK_P, down: false),
            Key(Win32.VK_MENU, down: false),
            Key(Win32.VK_CONTROL, down: false),
        };

        var sent = Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
        return sent == inputs.Length;
    }

    private static Win32.INPUT Key(ushort vk, bool down) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        U = new Win32.InputUnion
        {
            ki = new Win32.KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = down ? 0u : Win32.KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };
}
