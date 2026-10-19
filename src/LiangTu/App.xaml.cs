using System.Windows;
using LiangTu.Models;
using LiangTu.Services;
using LiangTu.Views;
using Microsoft.Win32;

namespace LiangTu;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private AppSettings _settings = null!;
    private Line _line = null!;
    private LineOverlayWindow _overlay = null!;
    private SaveAsAutomator? _saveAs;
    private ScreenshotWatcher? _watcher;
    private TrayService? _tray;
    private HotKeyService? _hotKey;
    private TopEdgeSensor? _edge;
    private bool _pinned;
    private DateTime _peekUntil = DateTime.MinValue;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\LiangTu.SingleInstance", out var created);
        if (!created)
        {
            System.Windows.MessageBox.Show("晾图已在运行。", "晾图", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppLog.Init();
        _settings = AppSettings.Load();
        if (_settings.LaunchAtLogin && !StartupService.IsEnabled)
            StartupService.Enable();

        try { ShellContextMenu.UninstallIfPresent(); }
        catch (Exception ex) { AppLog.Warn($"Shell menu cleanup: {ex.Message}"); }

        // Stop legacy PowerShell helper before automating Save-As.
        OldHelperGuard.PromptAndStopIfNeeded();

        _line = new Line(_settings);
        _overlay = new LineOverlayWindow(_line);
        AppIcon.ApplyToWindow(_overlay);
        _overlay.MonitorChanged += RememberMonitor;
        _overlay.EmptyIdleDock += () =>
        {
            _pinned = false;
            _peekUntil = DateTime.MinValue;
        };
        _line.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Line.PhotosFolded) && _line.PhotosFolded)
                _pinned = false;
        };
        _line.Notice += (title, body) => Dispatcher.BeginInvoke(() => _tray?.ShowBalloon(title, body));

        var startMon = !double.IsNaN(_settings.MonitorLeft)
            ? TopEdgeSensor.ResolvePreferredMonitor(_settings.MonitorLeft, _settings.MonitorTop, _settings.MonitorWidth)
            : TopEdgeSensor.GetMonitorUnderCursor();
        _overlay.PlaceOnMonitor(startMon);
        _overlay.Show();
        _overlay.SetRevealed(false, animate: false);

        _saveAs = new SaveAsAutomator(_settings);
        // Path-set failures are common on Tipray UI quirks; log only — don't spam balloons.
        _saveAs.Warning += msg => AppLog.Warn($"SaveAs: {msg}");

        _watcher = new ScreenshotWatcher(_settings);
        _watcher.NewScreenshot += path => _ = HangNewShotAsync(path);
        _watcher.Start();

        _hotKey = new HotKeyService(_overlay, _settings);
        _hotKey.Pressed += () => Dispatcher.Invoke(Toggle);
        _hotKey.ShotPressed += () => Dispatcher.BeginInvoke(() => _hotKey?.RelayTiprayShot());

        _tray = new TrayService(
            _settings,
            _line,
            Toggle,
            () => Shutdown(),
            ChooseInbox,
            () =>
            {
                Directory.CreateDirectory(_settings.InboxPath);
                System.Diagnostics.Process.Start("explorer.exe", _settings.InboxPath);
            },
            () => _watcher.Restart(),
            MoveToMonitor,
            () => _overlay.CurrentMonitor,
            () => _overlay.RequestToggleFold(),
            CleanupInbox,
            StopOldHelper,
            ApplyHotKey,
            () => _hotKey!.CurrentLabel,
            ApplyShotHotKey,
            SetShotHotKeyEnabled,
            () => _hotKey!.CurrentShotLabel,
            OpenLogs);

        _hotKey.Register();

        _edge = new TopEdgeSensor
        {
            IsBusy = () => _pinned || _line.DraggingId is not null || _line.PressedId is not null
                           || _overlay.IsRopeDragging || _overlay.IsOverflowOpen
                           || _overlay.IsLayoutAnimating
                           || DateTime.Now < _peekUntil,
            IsRevealed = () => _line.Revealed,
            GetLineZone = () => _overlay.GetZone(),
            GetLineMonitor = () => _overlay.CurrentMonitor
        };
        _edge.ShouldReveal += _ =>
        {
            _overlay.PlaceOnMonitor(ResolvePinnedMonitor());
            var compact = _line.IsVisuallyEmpty || _line.PhotosFolded;
            Reveal(pinned: !compact, peekSeconds: compact ? 2 : 0);
        };
        _edge.ShouldMoveToMonitor += _ => { };
        _edge.ShouldRetract += () =>
        {
            if (_pinned || DateTime.Now < _peekUntil || _overlay.IsRopeDragging) return;
            HideLine();
        };
        _edge.SetEnabled(true);

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Silent background prune — no balloon.
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try { InboxCleanup.Run(_settings, _line.LivePaths().ToList()); }
            catch (Exception ex) { AppLog.Warn($"Startup inbox cleanup: {ex.Message}"); }
        });

        ShowFirstRunIfNeeded();
        var compactStart = _line.IsVisuallyEmpty || _line.PhotosFolded;
        Reveal(pinned: !compactStart, peekSeconds: 4);
    }

    private void ShowFirstRunIfNeeded()
    {
        if (_settings.SeenFirstRun) return;
        _settings.SeenFirstRun = true;
        _settings.Save();
        System.Windows.MessageBox.Show(
            "欢迎使用晾图\n\n" +
            "1. 若曾用过「蓝盾截图助手」，请先停止它（托盘也可随时停止）\n" +
            "2. 截图可用晾图「截图快捷键」（默认 Ctrl+Alt+A，内部转发蓝盾 Ctrl+Alt+P），或直接按蓝盾原键\n" +
            "3. 截图会自动挂到屏幕顶绳；" + _hotKey!.CurrentLabel + " 或贴顶边可展开/收起\n\n" +
            "右键托盘图标可开关音效、微风、收件箱清理与快捷键。",
            $"晾图 v{AppVersion.Display}",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ApplyHotKey(Win32HotKeys.Preset preset)
    {
        if (_hotKey is null) return;
        var ok = _hotKey.TryApplyPreset(preset);
        // Only notify on failure — success is obvious from the menu checkmark.
        if (!ok)
            _tray?.ShowBalloon("显隐快捷键注册失败", "可能与其它软件冲突，请另选一组。");
        _tray?.RebuildMenu();
    }

    private void ApplyShotHotKey(Win32HotKeys.Preset preset)
    {
        if (_hotKey is null) return;
        var ok = _hotKey.TryApplyShotPreset(preset);
        if (!ok)
            _tray?.ShowBalloon("截图快捷键注册失败", "可能与显隐快捷键或其它软件冲突，请另选一组。");
        _tray?.RebuildMenu();
    }

    private void SetShotHotKeyEnabled(bool enabled)
    {
        _hotKey?.SetShotEnabled(enabled);
        _tray?.RebuildMenu();
    }

    private void StopOldHelper()
    {
        var n = OldHelperGuard.StopAll();
        if (n > 0)
            _tray?.ShowBalloon("已停止旧助手", $"已结束 {n} 个进程，并关闭其开机自启。");
    }

    private void CleanupInbox()
    {
        var n = InboxCleanup.Run(_settings, _line.LivePaths().ToList());
        if (n > 0)
            _tray?.ShowBalloon("清理完成", $"已将 {n} 个旧截图移入回收站。");
    }

    private void OpenLogs()
    {
        Directory.CreateDirectory(AppPaths.Logs);
        System.Diagnostics.Process.Start("explorer.exe", AppPaths.Logs);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            AppLog.Info("DisplaySettingsChanged — adapting rope");
            _overlay.AdaptToDisplayChanges();
            RememberMonitor(_overlay.CurrentMonitor);
        });
    }

    private void RememberMonitor(WpfRect mon)
    {
        _settings.MonitorLeft = mon.Left;
        _settings.MonitorTop = mon.Top;
        _settings.MonitorWidth = mon.Width;
        _settings.Save();
    }

    private void MoveToMonitor(WpfRect mon)
    {
        Dispatcher.Invoke(() =>
        {
            _overlay.PlaceOnMonitor(mon);
            RememberMonitor(mon);
            _edge?.SetEnabled(true);
            Reveal(pinned: true);
        });
    }

    private async Task HangNewShotAsync(string path)
    {
        try
        {
            await Dispatcher.InvokeAsync(() => _line.Prune());

            // Keep Tipray ciphertext as-is — never re-encode to cleartext PNG.
            // Tipray may still be writing/encrypting — retry without toasting failures.
            Guid? id = null;
            for (var attempt = 0; attempt < 5 && id is null; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(200);
                id = await Dispatcher.InvokeAsync(() => _line.Hang(path));
            }

            if (id is null)
            {
                AppLog.Warn($"Hang gave up: {path}");
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                _overlay.PlaceOnMonitor(ResolvePinnedMonitor(), announce: false);
                _edge?.SetEnabled(true);
                Reveal(peekSeconds: 2.5);
            });

            _ = Task.Run(() => InboxCleanup.Run(_settings, _line.LivePaths().ToList()));
        }
        catch (Exception ex)
        {
            AppLog.Error($"HangNewShot: {ex.Message}");
        }
    }

    private WpfRect ResolvePinnedMonitor()
    {
        if (!double.IsNaN(_settings.MonitorLeft))
            return TopEdgeSensor.ResolvePreferredMonitor(
                _settings.MonitorLeft, _settings.MonitorTop, _settings.MonitorWidth);

        if (_overlay.CurrentMonitor.Width > 1)
            return _overlay.CurrentMonitor;

        return TopEdgeSensor.GetMonitorUnderCursor();
    }

    private void Toggle()
    {
        if (_line.Revealed)
        {
            _pinned = false;
            _peekUntil = DateTime.MinValue;
            HideLine();
        }
        else
        {
            _overlay.PlaceOnMonitor(ResolvePinnedMonitor());
            _edge?.SetEnabled(true);
            Reveal(pinned: true);
        }
    }

    private void Reveal(bool pinned = false, double peekSeconds = 0)
    {
        var compact = _line.IsVisuallyEmpty || _line.PhotosFolded;
        if (compact) pinned = false;

        if (pinned) _pinned = true;
        else if (compact) _pinned = false;

        if (peekSeconds > 0) _peekUntil = DateTime.Now.AddSeconds(peekSeconds);
        if (!_overlay.IsVisible) _overlay.Show();
        _overlay.SetRevealed(true);
    }

    private void HideLine()
    {
        _pinned = false;
        _overlay.SetRevealed(false);
    }

    private void ChooseInbox()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择晾图收件箱目录",
            SelectedPath = _settings.InboxPath,
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _settings.InboxPath = dlg.SelectedPath;
        _settings.Save();
        Directory.CreateDirectory(_settings.InboxPath);
        _watcher?.Restart();
        AppLog.Info($"Inbox => {_settings.InboxPath}");
        _tray?.ShowBalloon("收件箱已更新", _settings.InboxPath);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _edge?.Dispose();
        _hotKey?.Dispose();
        _tray?.Dispose();
        _watcher?.Dispose();
        _saveAs?.Dispose();
        base.OnExit(e);
    }
}
