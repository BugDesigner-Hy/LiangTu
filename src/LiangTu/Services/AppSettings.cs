using System.Text.Json;

namespace LiangTu.Services;

public sealed class AppSettings
{
    public string InboxPath { get; set; } = AppPaths.DefaultInbox;
    public bool LaunchAtLogin { get; set; }
    public bool SoundOn { get; set; } = true;
    /// <summary>Idle breeze + gust sway on pegged photos.</summary>
    public bool BreezeSway { get; set; } = true;
    public bool AutomateSaveAs { get; set; } = true;
    public bool ForceInboxPath { get; set; } = true;

    /// <summary>Delete inbox images older than this many days (0 = off). Files on the rope are kept.</summary>
    public int InboxRetentionDays { get; set; } = 14;

    /// <summary>Keep at most this many inbox images by age (0 = off). Files on the rope are kept.</summary>
    public int InboxMaxFiles { get; set; } = 200;

    /// <summary>Win32 modifiers without MOD_NOREPEAT (e.g. MOD_CONTROL|MOD_ALT).</summary>
    public uint HotKeyModifiers { get; set; } = Win32HotKeys.DefaultModifiers;

    /// <summary>Virtual-key code (default 'T').</summary>
    public uint HotKeyVk { get; set; } = Win32HotKeys.DefaultVk;

    /// <summary>Custom shot hotkey that relays to Tipray Ctrl+Alt+P.</summary>
    public bool ShotHotKeyEnabled { get; set; } = true;

    /// <summary>Shot hotkey modifiers without MOD_NOREPEAT (default Ctrl+Alt).</summary>
    public uint ShotHotKeyModifiers { get; set; } = Win32HotKeys.DefaultShotModifiers;

    /// <summary>Shot hotkey VK (default 'A').</summary>
    public uint ShotHotKeyVk { get; set; } = Win32HotKeys.DefaultShotVk;

    public bool SeenFirstRun { get; set; }

    /// <summary>Clothespin look: Wood / Plastic / Metal / Vintage.</summary>
    public string PinStyle { get; set; } = "Wood";

    /// <summary>Rope material: Hemp / Cotton / Nylon / Wire.</summary>
    public string RopeStyle { get; set; } = "Hemp";

    /// <summary>Photo frame look: Card / Polaroid / Wood / Tape.</summary>
    public string PhotoStyle { get; set; } = "Card";

    /// <summary>Rope-end charms (multi): "None" or comma list e.g. "Bear,Sock,Star".</summary>
    public string CharmStyle { get; set; } = "None";

    /// <summary>Last rope monitor origin/size in DIPs (for restore across launches / display changes).</summary>
    public double MonitorLeft { get; set; } = double.NaN;
    public double MonitorTop { get; set; } = double.NaN;
    public double MonitorWidth { get; set; } = double.NaN;

    public static AppSettings Load()
    {
        AppPaths.EnsureCreated();
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s is not null)
                {
                    if (string.IsNullOrWhiteSpace(s.InboxPath))
                        s.InboxPath = AppPaths.DefaultInbox;
                    if (s.HotKeyModifiers == 0) s.HotKeyModifiers = Win32HotKeys.DefaultModifiers;
                    if (s.HotKeyVk == 0) s.HotKeyVk = Win32HotKeys.DefaultVk;
                    if (s.ShotHotKeyModifiers == 0) s.ShotHotKeyModifiers = Win32HotKeys.DefaultShotModifiers;
                    if (s.ShotHotKeyVk == 0) s.ShotHotKeyVk = Win32HotKeys.DefaultShotVk;
                    s.PinStyle = ThemeStyles.ParsePin(s.PinStyle).ToString();
                    s.RopeStyle = ThemeStyles.ParseRope(s.RopeStyle).ToString();
                    s.PhotoStyle = ThemeStyles.ParsePhoto(s.PhotoStyle).ToString();
                    s.CharmStyle = ThemeStyles.FormatCharms(ThemeStyles.ParseCharms(s.CharmStyle));
                    Directory.CreateDirectory(s.InboxPath);
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Load settings failed: {ex.Message}");
        }

        var fresh = new AppSettings();
        Directory.CreateDirectory(fresh.InboxPath);
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppPaths.SettingsFile, json);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Save settings failed: {ex.Message}");
        }
    }
}

/// <summary>Hotkey presets shared by settings + HotKeyService (avoids Win32 circular refs in JSON).</summary>
public static class Win32HotKeys
{
    public const uint DefaultModifiers = Native.Win32.MOD_CONTROL | Native.Win32.MOD_ALT;
    public const uint DefaultVk = 0x54; // T

    public const uint DefaultShotModifiers = Native.Win32.MOD_CONTROL | Native.Win32.MOD_ALT;
    public const uint DefaultShotVk = 0x41; // A

    public sealed record Preset(string Label, uint Modifiers, uint Vk);

    public static readonly Preset[] Presets =
    [
        new("Ctrl+Alt+T", Native.Win32.MOD_CONTROL | Native.Win32.MOD_ALT, 0x54),
        new("Ctrl+Shift+T", Native.Win32.MOD_CONTROL | Native.Win32.MOD_SHIFT, 0x54),
        new("Ctrl+Alt+L", Native.Win32.MOD_CONTROL | Native.Win32.MOD_ALT, 0x4C),
        new("Ctrl+Alt+H", Native.Win32.MOD_CONTROL | Native.Win32.MOD_ALT, 0x48),
    ];

    public static readonly Preset[] ShotPresets =
    [
        new("Ctrl+Alt+A", Native.Win32.MOD_CONTROL | Native.Win32.MOD_ALT, 0x41),
        new("Ctrl+Shift+A", Native.Win32.MOD_CONTROL | Native.Win32.MOD_SHIFT, 0x41),
        new("Ctrl+Alt+Q", Native.Win32.MOD_CONTROL | Native.Win32.MOD_ALT, 0x51),
        new("F8", 0, 0x77),
    ];

    public static string LabelFor(uint modifiers, uint vk)
    {
        var p = Presets.FirstOrDefault(x => x.Modifiers == modifiers && x.Vk == vk)
                ?? ShotPresets.FirstOrDefault(x => x.Modifiers == modifiers && x.Vk == vk);
        return p?.Label ?? $"自定义 (0x{modifiers:X}+0x{vk:X})";
    }

    public static string ShotLabelFor(uint modifiers, uint vk)
    {
        var p = ShotPresets.FirstOrDefault(x => x.Modifiers == modifiers && x.Vk == vk);
        return p?.Label ?? LabelFor(modifiers, vk);
    }
}
