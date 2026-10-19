using System.Drawing;
using System.Windows.Forms;
using LiangTu.Models;

namespace LiangTu.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AppSettings _settings;
    private readonly Line _line;
    private readonly Action _toggle;
    private readonly Action _quit;
    private readonly Action _chooseInbox;
    private readonly Action _openInbox;
    private readonly Action _restartWatcher;
    private readonly Action<WpfRect> _moveToMonitor;
    private readonly Func<WpfRect> _currentMonitor;
    private readonly Action _toggleFold;
    private readonly Action _cleanupInbox;
    private readonly Action _stopOldHelper;
    private readonly Action<Win32HotKeys.Preset> _setHotKey;
    private readonly Func<string> _hotKeyLabel;
    private readonly Action<Win32HotKeys.Preset> _setShotHotKey;
    private readonly Action<bool> _setShotHotKeyEnabled;
    private readonly Func<string> _shotHotKeyLabel;
    private readonly Action _openLogs;

    public TrayService(
        AppSettings settings,
        Line line,
        Action toggle,
        Action quit,
        Action chooseInbox,
        Action openInbox,
        Action restartWatcher,
        Action<WpfRect> moveToMonitor,
        Func<WpfRect> currentMonitor,
        Action toggleFold,
        Action cleanupInbox,
        Action stopOldHelper,
        Action<Win32HotKeys.Preset> setHotKey,
        Func<string> hotKeyLabel,
        Action<Win32HotKeys.Preset> setShotHotKey,
        Action<bool> setShotHotKeyEnabled,
        Func<string> shotHotKeyLabel,
        Action openLogs)
    {
        _settings = settings;
        _line = line;
        _toggle = toggle;
        _quit = quit;
        _chooseInbox = chooseInbox;
        _openInbox = openInbox;
        _restartWatcher = restartWatcher;
        _moveToMonitor = moveToMonitor;
        _currentMonitor = currentMonitor;
        _toggleFold = toggleFold;
        _cleanupInbox = cleanupInbox;
        _stopOldHelper = stopOldHelper;
        _setHotKey = setHotKey;
        _hotKeyLabel = hotKeyLabel;
        _setShotHotKey = setShotHotKey;
        _setShotHotKeyEnabled = setShotHotKeyEnabled;
        _shotHotKeyLabel = shotHotKeyLabel;
        _openLogs = openLogs;

        _icon = new NotifyIcon
        {
            Text = AppVersion.TrayText,
            Visible = true,
            Icon = AppIcon.GetTrayIcon()
        };
        _icon.DoubleClick += (_, _) => _toggle();
        RebuildMenu();
    }

    public void RebuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem("显示/隐藏晾衣绳", null, (_, _) => _toggle()) { Name = "toggle" });

        var foldLabel = _line.PhotosFolded ? "展开全部截图" : "折叠全部截图";
        menu.Items.Add(new ToolStripMenuItem(foldLabel, null, (_, _) => _toggleFold())
        {
            Name = "fold",
            Enabled = _line.LiveCount > 0
        });
        menu.Items.Add(new ToolStripMenuItem("全部取下", null, (_, _) => _line.Clear())
        {
            Name = "clear",
            Enabled = _line.LiveCount > 0
        });

        var screens = new ToolStripMenuItem("移到显示器") { Name = "screens" };
        menu.Items.Add(screens);

        menu.Items.Add(new ToolStripSeparator());

        var auto = new ToolStripMenuItem("自动确认蓝盾另存为") { Checked = _settings.AutomateSaveAs, CheckOnClick = true };
        auto.CheckedChanged += (_, _) =>
        {
            _settings.AutomateSaveAs = auto.Checked;
            _settings.Save();
        };
        menu.Items.Add(auto);

        var force = new ToolStripMenuItem("另存为写入收件箱") { Checked = _settings.ForceInboxPath, CheckOnClick = true };
        force.CheckedChanged += (_, _) =>
        {
            _settings.ForceInboxPath = force.Checked;
            _settings.Save();
        };
        menu.Items.Add(force);

        menu.Items.Add(new ToolStripMenuItem("选择收件箱目录…", null, (_, _) => _chooseInbox()));
        menu.Items.Add(new ToolStripMenuItem("打开收件箱", null, (_, _) => _openInbox()));
        menu.Items.Add(new ToolStripMenuItem("清理收件箱旧文件", null, (_, _) => _cleanupInbox()));

        var retention = new ToolStripMenuItem("收件箱保留策略");
        foreach (var (label, days, max) in new (string, int, int)[]
                 {
                     ("关闭自动清理", 0, 0),
                     ("保留 7 天 / 最多 100 张", 7, 100),
                     ("保留 14 天 / 最多 200 张（默认）", 14, 200),
                     ("保留 30 天 / 最多 500 张", 30, 500),
                 })
        {
            var d = days;
            var m = max;
            var item = new ToolStripMenuItem(label)
            {
                Checked = _settings.InboxRetentionDays == d && _settings.InboxMaxFiles == m
            };
            item.Click += (_, _) =>
            {
                _settings.InboxRetentionDays = d;
                _settings.InboxMaxFiles = m;
                _settings.Save();
            };
            retention.DropDownItems.Add(item);
        }
        menu.Items.Add(retention);

        menu.Items.Add(new ToolStripSeparator());

        var hotkeys = new ToolStripMenuItem("显隐快捷键");
        foreach (var preset in Win32HotKeys.Presets)
        {
            var p = preset;
            var item = new ToolStripMenuItem(p.Label)
            {
                Checked = _settings.HotKeyModifiers == p.Modifiers && _settings.HotKeyVk == p.Vk
            };
            item.Click += (_, _) => _setHotKey(p);
            hotkeys.DropDownItems.Add(item);
        }
        menu.Items.Add(hotkeys);

        var shotKeys = new ToolStripMenuItem("截图快捷键");
        var shotEnable = new ToolStripMenuItem("启用（转发为 Ctrl+Alt+P）")
        {
            Checked = _settings.ShotHotKeyEnabled,
            CheckOnClick = true
        };
        shotEnable.CheckedChanged += (_, _) => _setShotHotKeyEnabled(shotEnable.Checked);
        shotKeys.DropDownItems.Add(shotEnable);
        shotKeys.DropDownItems.Add(new ToolStripSeparator());
        foreach (var preset in Win32HotKeys.ShotPresets)
        {
            var p = preset;
            var item = new ToolStripMenuItem(p.Label)
            {
                Checked = _settings.ShotHotKeyEnabled
                          && _settings.ShotHotKeyModifiers == p.Modifiers
                          && _settings.ShotHotKeyVk == p.Vk
            };
            item.Click += (_, _) => _setShotHotKey(p);
            shotKeys.DropDownItems.Add(item);
        }
        menu.Items.Add(shotKeys);

        var login = new ToolStripMenuItem("开机自启") { Checked = StartupService.IsEnabled, CheckOnClick = true };
        login.CheckedChanged += (_, _) =>
        {
            StartupService.SetEnabled(login.Checked);
            _settings.LaunchAtLogin = login.Checked;
            _settings.Save();
        };
        menu.Items.Add(login);

        var sound = new ToolStripMenuItem("音效（系统星号提示音）") { Checked = _settings.SoundOn, CheckOnClick = true };
        sound.CheckedChanged += (_, _) =>
        {
            _settings.SoundOn = sound.Checked;
            _settings.Save();
        };
        menu.Items.Add(sound);

        var breeze = new ToolStripMenuItem("微风摆动") { Checked = _settings.BreezeSway, CheckOnClick = true };
        breeze.CheckedChanged += (_, _) => _line.SetBreezeSway(breeze.Checked);
        menu.Items.Add(breeze);

        var pinMenu = new ToolStripMenuItem("衣夹样式");
        foreach (var (id, label) in ThemeStyles.PinOptions)
        {
            var style = id;
            var item = new ToolStripMenuItem(label)
            {
                Checked = _line.CurrentPinStyle == style
            };
            item.Click += (_, _) =>
            {
                _line.SetPinStyle(style);
                RebuildMenu();
            };
            pinMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(pinMenu);

        var ropeMenu = new ToolStripMenuItem("绳子材质");
        foreach (var (id, label) in ThemeStyles.RopeOptions)
        {
            var style = id;
            var item = new ToolStripMenuItem(label)
            {
                Checked = _line.CurrentRopeStyle == style
            };
            item.Click += (_, _) =>
            {
                _line.SetRopeStyle(style);
                RebuildMenu();
            };
            ropeMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(ropeMenu);

        var photoMenu = new ToolStripMenuItem("照片样式");
        foreach (var (id, label) in ThemeStyles.PhotoOptions)
        {
            var style = id;
            var item = new ToolStripMenuItem(label)
            {
                Checked = _line.CurrentPhotoStyle == style
            };
            item.Click += (_, _) =>
            {
                _line.SetPhotoStyle(style);
                RebuildMenu();
            };
            photoMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(photoMenu);

        var charmMenu = new ToolStripMenuItem("绳头挂饰");
        charmMenu.DropDownItems.Add(new ToolStripMenuItem("全部关闭", null, (_, _) =>
        {
            _line.ClearCharms();
            RebuildMenu();
        })
        {
            Enabled = _line.HasCharms
        });
        charmMenu.DropDownItems.Add(new ToolStripSeparator());
        var selected = _line.CurrentCharmStyles;
        foreach (var (id, label) in ThemeStyles.CharmOptions)
        {
            var style = id;
            var item = new ToolStripMenuItem(label)
            {
                Checked = selected.Contains(style)
            };
            item.Click += (_, _) =>
            {
                _line.ToggleCharmStyle(style);
                RebuildMenu();
            };
            charmMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(charmMenu);

        menu.Items.Add(new ToolStripMenuItem("停止旧版蓝盾截图助手", null, (_, _) => _stopOldHelper()));
        menu.Items.Add(new ToolStripMenuItem("打开日志目录", null, (_, _) => _openLogs()));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem($"关于晾图  v{AppVersion.Display}") { Enabled = false });
        menu.Items.Add(new ToolStripMenuItem("退出晾图", null, (_, _) => _quit()));

        menu.Opening += (_, _) =>
        {
            foreach (ToolStripItem it in menu.Items)
            {
                if (it is not ToolStripMenuItem mi) continue;
                switch (mi.Name)
                {
                    case "toggle":
                        mi.Text = _line.Revealed
                            ? $"隐藏晾衣绳  ({_hotKeyLabel()})"
                            : $"显示晾衣绳  ({_hotKeyLabel()})";
                        break;
                    case "fold":
                        mi.Text = _line.PhotosFolded ? "展开全部截图" : "折叠全部截图";
                        mi.Enabled = _line.LiveCount > 0;
                        break;
                    case "clear":
                        mi.Enabled = _line.LiveCount > 0;
                        break;
                }

                if (mi.Text == "微风摆动")
                    mi.Checked = _settings.BreezeSway;
                if (mi.Text?.StartsWith("音效", StringComparison.Ordinal) ?? false)
                    mi.Checked = _settings.SoundOn;
            }

            screens.DropDownItems.Clear();
            var current = _currentMonitor();
            var all = TopEdgeSensor.GetAllMonitors();
            if (all.Count == 0)
            {
                screens.DropDownItems.Add(new ToolStripMenuItem("未检测到显示器") { Enabled = false });
            }
            else
            {
                foreach (var m in all)
                {
                    var bounds = m.Bounds;
                    var item = new ToolStripMenuItem(m.Label)
                    {
                        Checked = TopEdgeSensor.SameMonitor(bounds, current)
                    };
                    item.Click += (_, _) => _moveToMonitor(bounds);
                    screens.DropDownItems.Add(item);
                }
            }

            // Refresh hotkey checks
            if (hotkeys.DropDownItems.Count == Win32HotKeys.Presets.Length)
            {
                for (var i = 0; i < Win32HotKeys.Presets.Length; i++)
                {
                    var p = Win32HotKeys.Presets[i];
                    if (hotkeys.DropDownItems[i] is ToolStripMenuItem hi)
                        hi.Checked = _settings.HotKeyModifiers == p.Modifiers && _settings.HotKeyVk == p.Vk;
                }
            }

            // Refresh shot hotkey enable + preset checks (index 0 = enable, 1 = separator)
            if (shotKeys.DropDownItems.Count >= 2 + Win32HotKeys.ShotPresets.Length
                && shotKeys.DropDownItems[0] is ToolStripMenuItem shotEn)
            {
                shotEn.Checked = _settings.ShotHotKeyEnabled;
                for (var i = 0; i < Win32HotKeys.ShotPresets.Length; i++)
                {
                    var p = Win32HotKeys.ShotPresets[i];
                    if (shotKeys.DropDownItems[2 + i] is ToolStripMenuItem si)
                    {
                        si.Checked = _settings.ShotHotKeyEnabled
                                     && _settings.ShotHotKeyModifiers == p.Modifiers
                                     && _settings.ShotHotKeyVk == p.Vk;
                    }
                }
            }
        };

        _icon.ContextMenuStrip = menu;
        _icon.Text = AppVersion.TrayText;
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text.Length > 240 ? text[..240] : text;
            _icon.ShowBalloonTip(3200);
        }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
