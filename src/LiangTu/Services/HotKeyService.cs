using System.Windows;
using System.Windows.Interop;
using LiangTu.Native;

namespace LiangTu.Services;

public sealed class HotKeyService : IDisposable
{
    public const int HotKeyId = 0x4C54; // "LT" — reveal/hide
    public const int ShotHotKeyId = 0x4C55; // shot relay

    private readonly Window _window;
    private readonly AppSettings _settings;
    private HwndSource? _source;
    private bool _registered;
    private bool _shotRegistered;
    private bool _hooked;

    public event Action? Pressed;
    public event Action? ShotPressed;

    public HotKeyService(Window window, AppSettings settings)
    {
        _window = window;
        _settings = settings;
    }

    public string CurrentLabel => Win32HotKeys.LabelFor(_settings.HotKeyModifiers, _settings.HotKeyVk);
    public string CurrentShotLabel => Win32HotKeys.ShotLabelFor(_settings.ShotHotKeyModifiers, _settings.ShotHotKeyVk);

    public void Register()
    {
        var helper = new WindowInteropHelper(_window);
        helper.EnsureHandle();
        if (!_hooked)
        {
            _source = HwndSource.FromHwnd(helper.Handle);
            _source?.AddHook(WndProc);
            _hooked = true;
        }

        UnregisterOnly();
        UnregisterShotOnly();

        var mods = _settings.HotKeyModifiers | Win32.MOD_NOREPEAT;
        _registered = Win32.RegisterHotKey(helper.Handle, HotKeyId, mods, _settings.HotKeyVk);
        if (_registered) AppLog.Info($"Hotkey {CurrentLabel} registered");
        else AppLog.Warn($"Hotkey {CurrentLabel} registration failed");

        RegisterShot(helper.Handle);
    }

    private void RegisterShot(IntPtr hwnd)
    {
        if (!_settings.ShotHotKeyEnabled)
        {
            AppLog.Info("Shot hotkey disabled");
            return;
        }

        // Avoid colliding with reveal hotkey.
        if (_settings.ShotHotKeyModifiers == _settings.HotKeyModifiers
            && _settings.ShotHotKeyVk == _settings.HotKeyVk)
        {
            AppLog.Warn($"Shot hotkey {CurrentShotLabel} collides with reveal hotkey — skipped");
            return;
        }

        var mods = _settings.ShotHotKeyModifiers | Win32.MOD_NOREPEAT;
        _shotRegistered = Win32.RegisterHotKey(hwnd, ShotHotKeyId, mods, _settings.ShotHotKeyVk);
        if (_shotRegistered) AppLog.Info($"Shot hotkey {CurrentShotLabel} registered → Ctrl+Alt+P");
        else AppLog.Warn($"Shot hotkey {CurrentShotLabel} registration failed");
    }

    public bool TryApplyPreset(Win32HotKeys.Preset preset)
    {
        _settings.HotKeyModifiers = preset.Modifiers;
        _settings.HotKeyVk = preset.Vk;
        _settings.Save();
        Register();
        return _registered;
    }

    public bool TryApplyShotPreset(Win32HotKeys.Preset preset)
    {
        _settings.ShotHotKeyModifiers = preset.Modifiers;
        _settings.ShotHotKeyVk = preset.Vk;
        _settings.ShotHotKeyEnabled = true;
        _settings.Save();
        Register();
        return _shotRegistered;
    }

    public void SetShotEnabled(bool enabled)
    {
        _settings.ShotHotKeyEnabled = enabled;
        _settings.Save();
        Register();
    }

    /// <summary>Relay Tipray chord; briefly drop shot registration if it equals Ctrl+Alt+P.</summary>
    public void RelayTiprayShot()
    {
        var isSelf = _settings.ShotHotKeyModifiers == (Win32.MOD_CONTROL | Win32.MOD_ALT)
                     && _settings.ShotHotKeyVk == Win32.VK_P;
        if (isSelf && _shotRegistered)
        {
            UnregisterShotOnly();
            try { TiprayShot.SendCtrlAltP(); }
            finally
            {
                var helper = new WindowInteropHelper(_window);
                if (helper.Handle != IntPtr.Zero)
                    RegisterShot(helper.Handle);
            }
            return;
        }

        TiprayShot.SendCtrlAltP();
    }

    private void UnregisterOnly()
    {
        if (!_registered) return;
        try
        {
            var helper = new WindowInteropHelper(_window);
            Win32.UnregisterHotKey(helper.Handle, HotKeyId);
        }
        catch { /* ignore */ }
        _registered = false;
    }

    private void UnregisterShotOnly()
    {
        if (!_shotRegistered) return;
        try
        {
            var helper = new WindowInteropHelper(_window);
            Win32.UnregisterHotKey(helper.Handle, ShotHotKeyId);
        }
        catch { /* ignore */ }
        _shotRegistered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Win32.WM_HOTKEY) return IntPtr.Zero;
        var id = wParam.ToInt32();
        if (id == HotKeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        else if (id == ShotHotKeyId)
        {
            ShotPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterOnly();
        UnregisterShotOnly();
        if (_hooked)
        {
            _source?.RemoveHook(WndProc);
            _hooked = false;
        }
    }
}
