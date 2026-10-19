using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LiangTu.Models;
using LiangTu.Native;
using LiangTu.Services;

namespace LiangTu.Views;

public partial class LineOverlayWindow : Window
{
    public const string PeggedIdFormat = "LiangTu.PeggedId";

    private const double CardWidth = 148;
    private const double CardHalf = CardWidth / 2;
    private const double MinSpacing = 158;
    private const double FullPanelHeight = 220;
    private const double EmptyPanelHeight = 44;
    private const double EmptyWindowWidth = 48;
    private const double FoldedPanelHeight = 118;
    private const double FoldedWindowWidth = 132;
    /// <summary>How long compact rope (empty / folded) stays out before tucking off-screen.</summary>
    private const double CompactIdleDockSeconds = 12;
    /// <summary>Pixels that remain peeking when docked.</summary>
    private const double CompactPeekPixels = 12;

    private enum ChromeKind { Full, Folded, Empty }

    private readonly Line _line;
    private readonly Dictionary<Guid, PeggedCard> _cards = new();
    private bool _passThrough = true;
    private DispatcherTimer? _hitTimer;
    private DispatcherTimer? _idleDockTimer;
    private WpfRect _monitor;
    private bool _ropeDragging;
    private WpfRect? _dragLastMonitor;
    private bool _overflowPopupOpen;
    private bool _layoutAnimating;
    private ChromeKind _chromeKind = ChromeKind.Full;
    private readonly HashSet<Guid> _dismissing = new();
    private DateTime _lastOverInteractive = DateTime.MinValue;
    private int? _dragInsertIndex;
    private string _charmGlyphKey = "";
    private bool _charmSwayRunning;
    private readonly List<RotateTransform> _charmSwings = [];
    private double _ropeX0 = 40;
    private double _ropeX1 = 200;
    private double _ropeY = 18;
    private double _ropeSag = 12;
    private const double RopePinOffset = 8;

    public double PanelHeight { get; private set; } = FullPanelHeight;
    public WpfRect CurrentMonitor => _monitor;
    public bool IsRopeDragging => _ropeDragging;
    public bool IsOverflowOpen => _overflowPopupOpen || OverflowPopup.IsOpen;
    public bool IsLayoutAnimating => _layoutAnimating;
    /// <summary>Canvas that hosts pegged cards (local drag coordinates).</summary>
    public Canvas CardsHostPublic => CardsHost;
    /// <summary>Default Y near rope ends; prefer CardTopOnRope(centerX) for mid-span.</summary>
    public double RopeCardTop => _ropeY - RopePinOffset;

    public event Action<WpfRect>? MonitorChanged;
    /// <summary>Empty knot was idle too long — host should unpin and allow dock.</summary>
    public event Action? EmptyIdleDock;

    public LineOverlayWindow(Line line)
    {
        InitializeComponent();
        _line = line;
        _line.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            Rebuild();
            SyncIdleDockTimer();
        }, DispatcherPriority.Render);
        _line.ItemAdded += _ => Dispatcher.BeginInvoke(() =>
        {
            Rebuild();
            SyncIdleDockTimer();
        }, DispatcherPriority.Render);
        _line.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Line.DraggingId))
                Dispatcher.BeginInvoke(UpdateDraggingCardChrome, DispatcherPriority.Render);
            if (e.PropertyName is nameof(Line.Revealed) or nameof(Line.PhotosFolded))
                Dispatcher.BeginInvoke(SyncIdleDockTimer, DispatcherPriority.Background);
            if (e.PropertyName == nameof(Line.BreezeSwayEnabled))
                Dispatcher.BeginInvoke(SyncCharmSway, DispatcherPriority.Background);
        };
        _line.ThemeChanged += () => Dispatcher.BeginInvoke(() =>
        {
            ApplyRopeStyle();
            ApplyCharmStyle();
            PlaceCharms(
                _line.IsVisuallyEmpty,
                _line.PhotosFolded && !_line.HasFallingItems,
                ActualWidth > 1 ? ActualWidth : Width);
        }, DispatcherPriority.Render);
        SourceInitialized += (_, _) => ApplyToolWindowStyles();
        Loaded += (_, _) =>
        {
            ApplyRopeStyle();
            ApplyCharmStyle();
            Rebuild();
            _hitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _hitTimer.Tick += (_, _) => UpdatePassThrough();
            _hitTimer.Start();
        };
        SizeChanged += (_, _) => Rebuild();

        AllowDrop = true;
        PreviewDragOver += Window_OnPreviewDragOver;
        Drop += Window_OnDrop;
        DragLeave += Window_OnDragLeave;

        WireRopeHandle(RopeCapL);
        WireRopeHandle(RopeCapR);
        WireRopeHandle(RopeKnot);
        ApplyRopeStyle();
        ApplyCharmStyle();
    }

    public void ApplyRopeStyle()
    {
        var p = ThemeStyles.Rope(_line.CurrentRopeStyle);
        Rope.Stroke = ThemeStyles.Brush(p.Stroke);
        Rope.StrokeThickness = p.Thickness;
        RopeShade.Stroke = ThemeStyles.Brush(p.Shade);
        RopeShade.StrokeThickness = p.ShadeThickness;

        var wood = ThemeStyles.Brush(p.CapWood);
        var woodDark = ThemeStyles.Brush(p.CapWoodDark);
        var knot = ThemeStyles.Brush(p.CapKnot);
        var knotHi = ThemeStyles.Brush(p.CapKnotLight);

        CapLPlate.Background = wood;
        CapLPlate.BorderBrush = woodDark;
        CapLCleat.Fill = knotHi;
        CapLCleat.Stroke = woodDark;
        CapLKnot.Fill = knot;
        CapLKnot.Stroke = woodDark;
        CapLKnotHi.Fill = knotHi;
        CapLKnotHi.Stroke = woodDark;
        CapLStub.Stroke = knot;
        CapLStub.StrokeThickness = Math.Max(2, p.Thickness);

        CapRPlate.Background = wood;
        CapRPlate.BorderBrush = woodDark;
        CapRCleat.Fill = knotHi;
        CapRCleat.Stroke = woodDark;
        CapRKnot.Fill = knot;
        CapRKnot.Stroke = woodDark;
        CapRKnotHi.Fill = knotHi;
        CapRKnotHi.Stroke = woodDark;
        CapRStub.Stroke = knot;
        CapRStub.StrokeThickness = Math.Max(2, p.Thickness);

        KnotRingOuter.Stroke = woodDark;
        KnotRingInner.Stroke = knotHi;
        KnotCore.Fill = knot;
        KnotCore.Stroke = woodDark;
        KnotCoreHi.Fill = knotHi;
        KnotCoreHi.Stroke = woodDark;
        KnotPeg.Background = wood;
        KnotPeg.BorderBrush = woodDark;
    }

    private void WireRopeHandle(UIElement el)
    {
        el.PreviewMouseLeftButtonDown += Rope_OnDown;
        el.PreviewMouseMove += Rope_OnMove;
        el.PreviewMouseLeftButtonUp += Rope_OnUp;
    }

    public void PlaceOnMonitor(WpfRect monitorDip, bool announce = true)
    {
        var changed = _monitor.Width < 1 || !TopEdgeSensor.SameMonitor(_monitor, monitorDip);

        _monitor = monitorDip;
        BeginAnimation(TopProperty, null);
        ApplyWindowChrome(monitorDip);

        AppLog.Info($"PlaceOnMonitor dip=({monitorDip.Left:0},{monitorDip.Top:0},{monitorDip.Width:0}x{monitorDip.Height:0}) empty={_line.LiveCount == 0} revealed={_line.Revealed}");

        UpdateLayout();
        Rebuild();
        // After moving, stay click-through until cursor is over a card/cap.
        SetPassThrough(true);

        if (changed && announce)
            MonitorChanged?.Invoke(monitorDip);
    }

    private ChromeKind DesiredChrome() =>
        _line.IsVisuallyEmpty ? ChromeKind.Empty
        : _line.PhotosFolded ? ChromeKind.Folded
        : ChromeKind.Full;

    /// <summary>Empty / folded → compact on the right; expanded photos → full monitor width.</summary>
    private void ApplyWindowChrome(WpfRect monitorDip)
    {
        _chromeKind = DesiredChrome();

        switch (_chromeKind)
        {
            case ChromeKind.Empty:
                PanelHeight = EmptyPanelHeight;
                Width = EmptyWindowWidth;
                Left = monitorDip.Left + monitorDip.Width - EmptyWindowWidth - 12;
                break;
            case ChromeKind.Folded:
                PanelHeight = FoldedPanelHeight;
                Width = FoldedWindowWidth;
                Left = monitorDip.Left + monitorDip.Width - FoldedWindowWidth - 12;
                break;
            default:
                PanelHeight = FullPanelHeight;
                Width = monitorDip.Width;
                Left = monitorDip.Left;
                break;
        }

        Height = PanelHeight;
        Top = _line.Revealed ? monitorDip.Top : DockedTop(monitorDip);
    }

    public void AdaptToDisplayChanges()
    {
        if (_monitor.Width < 1)
        {
            PlaceOnMonitor(TopEdgeSensor.GetMonitorUnderCursor());
            return;
        }

        var resolved = TopEdgeSensor.ResolvePreferredMonitor(_monitor.Left, _monitor.Top, _monitor.Width);
        PlaceOnMonitor(resolved);
        if (_line.Revealed)
            SetRevealed(true, animate: false);
    }

    public void SetRevealed(bool revealed, bool animate = true)
    {
        if (_monitor.Width < 1)
            _monitor = TopEdgeSensor.GetMonitorUnderCursor();

        var shownTop = _monitor.Top;
        var hiddenTop = DockedTop(_monitor);

        BeginAnimation(TopProperty, null);

        if (!animate)
        {
            Top = revealed ? shownTop : hiddenTop;
            _line.Revealed = revealed;
            SetPassThrough(true);
            if (revealed) SyncIdleDockTimer();
            else CancelIdleDockTimer();
            return;
        }

        var anim = new DoubleAnimation
        {
            To = revealed ? shownTop : hiddenTop,
            Duration = TimeSpan.FromMilliseconds(revealed ? 280 : 360),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) =>
        {
            SetPassThrough(true);
            Rebuild();
        };
        BeginAnimation(TopProperty, anim);
        _line.Revealed = revealed;
        // Never block the desktop by default — UpdatePassThrough enables hits only over cards/caps.
        SetPassThrough(true);
        if (revealed) SyncIdleDockTimer();
        else CancelIdleDockTimer();
    }

    private bool IsCompactMode => DesiredChrome() is ChromeKind.Empty or ChromeKind.Folded;

    /// <summary>Compact modes leave a larger peek so the knot / stack tip stays findable.</summary>
    private double DockedTop(WpfRect monitorDip)
    {
        var peek = IsCompactMode ? CompactPeekPixels : 4;
        return monitorDip.Top - PanelHeight + peek;
    }

    private void SyncIdleDockTimer()
    {
        if (!_line.Revealed || !IsCompactMode)
        {
            CancelIdleDockTimer();
            return;
        }

        _idleDockTimer ??= new DispatcherTimer();
        _idleDockTimer.Stop();
        _idleDockTimer.Interval = TimeSpan.FromSeconds(CompactIdleDockSeconds);
        _idleDockTimer.Tick -= IdleDockTimer_OnTick;
        _idleDockTimer.Tick += IdleDockTimer_OnTick;
        _idleDockTimer.Start();
    }

    private void CancelIdleDockTimer()
    {
        _idleDockTimer?.Stop();
    }

    private void IdleDockTimer_OnTick(object? sender, EventArgs e)
    {
        CancelIdleDockTimer();
        if (!_line.Revealed || !IsCompactMode) return;
        if (_ropeDragging) { SyncIdleDockTimer(); return; }

        AppLog.Info($"Compact rope idle ({DesiredChrome()}) — dock off-screen (peek)");
        EmptyIdleDock?.Invoke();
        SetRevealed(false);
    }

    /// <summary>User touched the compact rope — keep it out a bit longer.</summary>
    public void PingEmptyIdle()
    {
        if (IsCompactMode && _line.Revealed)
            SyncIdleDockTimer();
    }

    public WpfRect GetZone() => new(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);

    private void Rope_OnDown(object sender, MouseButtonEventArgs e)
    {
        if (!_line.Revealed) return;
        PingEmptyIdle();
        _ropeDragging = true;
        _dragLastMonitor = _monitor;
        ((UIElement)sender).CaptureMouse();
        SetPassThrough(false);
        e.Handled = true;
    }

    private void Rope_OnMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_ropeDragging || e.LeftButton != MouseButtonState.Pressed) return;

        var mon = TopEdgeSensor.GetMonitorUnderCursor();
        if (_dragLastMonitor is null || !TopEdgeSensor.SameMonitor(_dragLastMonitor.Value, mon))
        {
            _dragLastMonitor = mon;
            PlaceOnMonitor(mon, announce: false);
            SetRevealed(true, animate: false);
            SetPassThrough(false);
        }
        e.Handled = true;
    }

    private void Rope_OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_ropeDragging) return;
        _ropeDragging = false;
        if (((UIElement)sender).IsMouseCaptured)
            ((UIElement)sender).ReleaseMouseCapture();

        var mon = TopEdgeSensor.GetMonitorUnderCursor();
        PlaceOnMonitor(mon);
        SetRevealed(true, animate: false);
        SetPassThrough(true);
        e.Handled = true;
    }

    private void ApplyToolWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        ex |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_LAYERED;
        ex &= ~Win32.WS_EX_NOACTIVATE;
        Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, ex);
        SetPassThrough(true);
    }

    private void SetPassThrough(bool pass)
    {
        if (_passThrough == pass)
        {
            // Still re-apply in case another call cleared the style.
        }
        _passThrough = pass;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        if (pass) ex |= Win32.WS_EX_TRANSPARENT;
        else ex &= ~Win32.WS_EX_TRANSPARENT;
        Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, ex);
    }

    private void UpdatePassThrough()
    {
        // While we are dragging our own content, keep the window interactive.
        if (_ropeDragging || _line.DraggingId is not null)
        {
            if (_passThrough) SetPassThrough(false);
            return;
        }

        // Hidden above the screen: always click-through.
        if (!_line.Revealed)
        {
            if (!_passThrough) SetPassThrough(true);
            return;
        }

        // Context menu / overflow popup — keep interactive.
        if (_overflowPopupOpen || _cards.Values.Any(c => c.ContextMenu?.IsOpen == true))
        {
            if (_passThrough) SetPassThrough(false);
            return;
        }

        var over = IsPointerOverInteractive();
        if (over) _lastOverInteractive = DateTime.Now;

        // Keep interactive briefly after leaving a card so click/drag isn't lost mid-gesture.
        var wantInteractive = over
            || (DateTime.Now - _lastOverInteractive).TotalMilliseconds < 200;

        if (wantInteractive)
        {
            if (_passThrough) SetPassThrough(false);
        }
        else
        {
            if (!_passThrough) SetPassThrough(true);
        }
    }

    private bool IsPointerOverInteractive()
    {
        if (!Win32.GetCursorPos(out var pt)) return false;

        // Prefer element screen bounds via PointToScreen (handles DPI correctly).
        if (IsPhysicalCursorOverElement(pt, out var cursorDip))
        {
            foreach (var card in _cards.Values)
            {
                if (card.Visibility != Visibility.Visible || !card.IsLoaded || card.ActualWidth <= 0) continue;
                if (IsCursorOverElementScreen(card, cursorDip, pad: 12)) return true;
            }

            if (OverflowBadge.Visibility == Visibility.Visible
                && IsCursorOverElementScreen(OverflowBadge, cursorDip, pad: 8))
                return true;

            if (FoldButton.Visibility == Visibility.Visible
                && IsCursorOverElementScreen(FoldButton, cursorDip, pad: 6))
                return true;

            if (RopeCapL.Visibility == Visibility.Visible
                && IsCursorOverElementScreen(RopeCapL, cursorDip, pad: 10)) return true;
            if (RopeCapR.Visibility == Visibility.Visible
                && IsCursorOverElementScreen(RopeCapR, cursorDip, pad: 10)) return true;
            if (RopeKnot.Visibility == Visibility.Visible
                && IsCursorOverElementScreen(RopeKnot, cursorDip, pad: 10)) return true;

        }

        // Fallback: canvas layout slots in window-local DIP (ignore card tilt).
        var local = ScreenPhysicalToLocal(pt);
        if (local is null) return false;

        foreach (var card in _cards.Values)
        {
            if (card.Visibility != Visibility.Visible) continue;
            var x = Canvas.GetLeft(card);
            var y = Canvas.GetTop(card);
            if (double.IsNaN(x) || double.IsNaN(y)) continue;
            var w = card.ActualWidth > 0 ? card.ActualWidth : CardWidth;
            var h = card.ActualHeight > 0 ? card.ActualHeight : 178;
            var r = new WpfRect(x - 8, y - 8, w + 16, h + 16);
            if (r.Contains(local.Value)) return true;
        }

        if (OverflowBadge.Visibility == Visibility.Visible)
        {
            var x = Canvas.GetLeft(OverflowBadge);
            var y = Canvas.GetTop(OverflowBadge);
            if (!double.IsNaN(x) && !double.IsNaN(y)
                && new WpfRect(x - 6, y - 6, OverflowBadge.Width + 12, OverflowBadge.Height + 12).Contains(local.Value))
                return true;
        }

        if (FoldButton.Visibility == Visibility.Visible)
        {
            var x = Canvas.GetLeft(FoldButton);
            var y = Canvas.GetTop(FoldButton);
            if (!double.IsNaN(x) && !double.IsNaN(y)
                && new WpfRect(x - 4, y - 4, FoldButton.Width + 8, FoldButton.Height + 8).Contains(local.Value))
                return true;
        }

        if (RopeKnot.Visibility == Visibility.Visible)
        {
            var x = Canvas.GetLeft(RopeKnot);
            var y = Canvas.GetTop(RopeKnot);
            if (!double.IsNaN(x) && !double.IsNaN(y)
                && new WpfRect(x - 6, y - 6, RopeKnot.Width + 12, RopeKnot.Height + 12).Contains(local.Value))
                return true;
        }

        return false;
    }

    private bool IsPhysicalCursorOverElement(Win32.POINT pt, out WpfPoint cursorDip)
    {
        cursorDip = default;
        try
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is null) return false;
            // GetCursorPos = physical pixels → DIP screen coordinates (do NOT also call PointFromScreen).
            cursorDip = source.CompositionTarget.TransformFromDevice.Transform(new WpfPoint(pt.X, pt.Y));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCursorOverElementScreen(FrameworkElement el, WpfPoint cursorDip, double pad)
    {
        try
        {
            if (!el.IsVisible || el.ActualWidth <= 0 || el.ActualHeight <= 0) return false;
            var tl = el.PointToScreen(new WpfPoint(0, 0));
            var br = el.PointToScreen(new WpfPoint(el.ActualWidth, el.ActualHeight));
            var r = new WpfRect(tl, br);
            r.Inflate(pad, pad);
            return r.Contains(cursorDip);
        }
        catch
        {
            return false;
        }
    }

    private WpfPoint? ScreenPhysicalToLocal(Win32.POINT pt)
    {
        try
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is null) return null;
            var screenDip = source.CompositionTarget.TransformFromDevice.Transform(new WpfPoint(pt.X, pt.Y));
            // Window.Left/Top are DIP; subtract to get window-local coordinates.
            return new WpfPoint(screenDip.X - Left, screenDip.Y - Top);
        }
        catch
        {
            return null;
        }
    }

    private void Rebuild()
    {
        // Keep falling cards on-canvas until dismiss animation finishes.
        var keepIds = _line.Items.Select(i => i.Id).ToHashSet();
        foreach (var id in _cards.Keys.ToList())
        {
            if (keepIds.Contains(id)) continue;

            if (_dismissing.Contains(id)) continue;

            CardsHost.Children.Remove(_cards[id]);
            _cards.Remove(id);
            _line.HitRects.Remove(id);
        }

        foreach (var item in _line.Items.Where(i => i.Falling))
        {
            if (!_cards.TryGetValue(item.Id, out var card)) continue;
            if (_dismissing.Contains(item.Id)) continue;
            _dismissing.Add(item.Id);
            card.PlayDismiss(() =>
            {
                CardsHost.Children.Remove(card);
                _cards.Remove(item.Id);
                _dismissing.Remove(item.Id);
                _line.HitRects.Remove(item.Id);
                // Last leaf finished: collapse to empty knot if nothing left.
                if (_line.IsVisuallyEmpty)
                    Dispatcher.BeginInvoke(Rebuild, DispatcherPriority.Render);
            });
        }

        if (_layoutAnimating) return;

        // Resize chrome when empty / folded / full mode changes.
        // Defer Empty chrome until fall animation items are gone (IsVisuallyEmpty).
        if (_monitor.Width > 1 && DesiredChrome() != _chromeKind)
            ApplyWindowChrome(_monitor);
        else if (_monitor.Width > 1 && Math.Abs(Height - PanelHeight) > 0.5)
            ApplyWindowChrome(_monitor);

        var layoutWidth = ActualWidth > 1 ? ActualWidth : Width;
        if (layoutWidth < 1) layoutWidth = _monitor.Width > 1 ? _monitor.Width : SystemParameters.PrimaryScreenWidth;

        // Keep full rope while the last photo is still falling (leaf dismiss).
        var empty = _line.IsVisuallyEmpty;
        var folded = _line.PhotosFolded && !_line.HasFallingItems;
        LayoutRope(empty, folded, layoutWidth);
        PlaceCharms(empty, folded, layoutWidth);

        var visible = empty ? Array.Empty<Pegged>() : _line.VisibleItems();
        var overflow = empty ? Array.Empty<Pegged>() : _line.OverflowItems();

        foreach (var item in _line.Items.Where(i => !i.Falling))
        {
            if (!_cards.TryGetValue(item.Id, out var card))
            {
                card = new PeggedCard(_line, item, this);
                _cards[item.Id] = card;
                CardsHost.Children.Add(card);
            }
        }

        var overflowReserve = !folded && overflow.Count > 0 ? 72 : 0;
        var layoutInnerWidth = Math.Max(200, layoutWidth - overflowReserve);

        for (var i = 0; i < visible.Count; i++)
        {
            var item = visible[i];
            var card = _cards[item.Id];
            if (_dismissing.Contains(item.Id)) continue;
            // Don't yank the card out from under a local reorder / lift drag.
            if (card.IsLocalDragging) { card.Visibility = Visibility.Visible; continue; }
            card.ResetMotionChrome();
            card.Visibility = Visibility.Visible;
            var centerX = LayoutCenterX(i, visible.Count, layoutInnerWidth);
            if (overflow.Count > 0)
                centerX = centerX - 8;
            Canvas.SetLeft(card, centerX - CardHalf);
            Canvas.SetTop(card, CardTopOnRope(centerX));
        }

        foreach (var item in overflow)
        {
            if (_cards.TryGetValue(item.Id, out var card) && !_dismissing.Contains(item.Id))
                card.Visibility = Visibility.Collapsed;
        }

        UpdateFoldButton(layoutWidth);
        UpdateOverflowBadge(overflow, layoutWidth, folded);
        UpdateDraggingCardChrome();
        UpdateLayout();

        foreach (var card in _cards.Values.Where(c => c.Visibility == Visibility.Visible))
            card.UpdateHitRect(RootCanvas);
    }

    public void RequestToggleFold()
    {
        if (_layoutAnimating || _line.LiveCount == 0) return;
        if (_line.PhotosFolded)
            AnimateExpand();
        else
            AnimateFold();
    }

    private void AnimateFold()
    {
        if (_layoutAnimating) return;
        _layoutAnimating = true;
        if (OverflowPopup.IsOpen) OverflowPopup.IsOpen = false;

        var layoutWidth = ActualWidth > 1 ? ActualWidth : Width;
        var badgeLeft = Math.Max(80, layoutWidth - 108);
        var gatherLeft = badgeLeft + 4;
        var gatherTop = 28;
        var visible = _line.VisibleItems().ToList();

        // Prepare stack badge under cards (fades in as cards gather).
        UpdateOverflowBadge(_line.Items.Where(i => !i.Falling).ToList(), layoutWidth, folded: true);
        OverflowBadge.Opacity = 0;
        OverflowBadge.Visibility = Visibility.Visible;
        OverflowBadge.RenderTransformOrigin = new WpfPoint(0.5, 0.15);
        OverflowBadge.RenderTransform = new ScaleTransform(0.7, 0.7);

        var remaining = new[] { visible.Count };
        if (remaining[0] == 0)
        {
            FinishFoldIn();
            return;
        }

        for (var i = 0; i < visible.Count; i++)
        {
            var item = visible[i];
            if (!_cards.TryGetValue(item.Id, out var card))
            {
                if (System.Threading.Interlocked.Decrement(ref remaining[0]) == 0)
                    FinishFoldIn();
                continue;
            }
            var delay = i * 28;
            card.AnimateTo(gatherLeft, gatherTop, opacity: 0, scale: 0.35, delayMs: delay, durationMs: 320, onCompleted: () =>
            {
                card.Visibility = Visibility.Collapsed;
                if (System.Threading.Interlocked.Decrement(ref remaining[0]) == 0)
                    FinishFoldIn();
            });
        }

        var badgeFade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(280))
        {
            BeginTime = TimeSpan.FromMilliseconds(120),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var badgeScale = new DoubleAnimation(1, TimeSpan.FromMilliseconds(320))
        {
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
        };
        OverflowBadge.BeginAnimation(OpacityProperty, badgeFade);
        if (OverflowBadge.RenderTransform is ScaleTransform st)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, badgeScale);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, badgeScale);
        }

        void FinishFoldIn()
        {
            _line.SetFolded(true);
            _layoutAnimating = false;
            if (_monitor.Width > 1)
                ApplyWindowChrome(_monitor); // shorten rope to the right
            Rebuild();
            OverflowBadge.BeginAnimation(OpacityProperty, null);
            OverflowBadge.Opacity = 1;
            SyncIdleDockTimer();
        }
    }

    private void AnimateExpand()
    {
        if (_layoutAnimating) return;
        _layoutAnimating = true;
        if (OverflowPopup.IsOpen) OverflowPopup.IsOpen = false;
        CancelIdleDockTimer();

        // Screen position of the stack before we expand chrome (absolute).
        var stackScreenLeft = Left + (double.IsNaN(Canvas.GetLeft(OverflowBadge)) ? 0 : Canvas.GetLeft(OverflowBadge));

        _line.SetFolded(false);
        if (_monitor.Width > 1)
            ApplyWindowChrome(_monitor); // restore full-width rope first

        var layoutWidth = ActualWidth > 1 ? ActualWidth : Width;
        var originLeft = Math.Clamp(stackScreenLeft - Left, 0, Math.Max(0, layoutWidth - 64));
        var originTop = 28;

        var visible = _line.VisibleItems().ToList();
        var overflow = _line.OverflowItems();
        var overflowReserve = overflow.Count > 0 ? 72 : 0;
        var layoutInnerWidth = Math.Max(200, layoutWidth - overflowReserve);

        // Fade out stack while cards scatter.
        var badgeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        badgeOut.Completed += (_, _) =>
        {
            OverflowBadge.Visibility = Visibility.Collapsed;
            OverflowBadge.BeginAnimation(OpacityProperty, null);
            OverflowBadge.Opacity = 1;
        };
        OverflowBadge.BeginAnimation(OpacityProperty, badgeOut);

        var remaining = new[] { visible.Count };
        if (remaining[0] == 0)
        {
            _layoutAnimating = false;
            Rebuild();
            return;
        }

        for (var i = 0; i < visible.Count; i++)
        {
            var item = visible[i];
            if (!_cards.TryGetValue(item.Id, out var card))
            {
                card = new PeggedCard(_line, item, this);
                _cards[item.Id] = card;
                CardsHost.Children.Add(card);
            }

            var centerX = LayoutCenterX(i, visible.Count, layoutInnerWidth);
            if (overflow.Count > 0) centerX -= 8;
            var destLeft = centerX - CardHalf;
            var destTop = CardTopOnRope(centerX);

            card.BeginAnimation(Canvas.LeftProperty, null);
            card.BeginAnimation(Canvas.TopProperty, null);
            card.BeginAnimation(OpacityProperty, null);
            Canvas.SetLeft(card, originLeft);
            Canvas.SetTop(card, originTop);
            card.Opacity = 0;
            card.Visibility = Visibility.Visible;
            card.SetScaleImmediate(0.35);

            var delay = i * 32;
            card.AnimateTo(destLeft, destTop, opacity: 1, scale: 1, delayMs: delay, durationMs: 360, onCompleted: () =>
            {
                if (System.Threading.Interlocked.Decrement(ref remaining[0]) == 0)
                {
                    _layoutAnimating = false;
                    Rebuild();
                }
            });
        }
    }

    private void UpdateDraggingCardChrome()
    {
        foreach (var (id, card) in _cards)
        {
            if (_line.DraggingId == id)
                card.Opacity = card.IsLocalDragging ? 1 : 0.38;
            else
                card.Opacity = 1;
        }
    }

    /// <summary>Card top near the clothesline → reorder / clip-on zone.</summary>
    public bool IsNearRope(double cardTop) => cardTop < 72;

    public int EstimateInsertIndexPublic(double canvasX) => EstimateInsertIndex(canvasX);

    public void OnLocalCardDragEnded() => UpdateDraggingCardChrome();

    private static bool TryGetPeggedId(System.Windows.IDataObject data, out Guid id)
    {
        id = Guid.Empty;
        try
        {
            if (data.GetDataPresent(PeggedIdFormat) && data.GetData(PeggedIdFormat) is Guid g)
            {
                id = g;
                return true;
            }
            if (data.GetDataPresent(PeggedIdFormat) && data.GetData(PeggedIdFormat) is string s
                && Guid.TryParse(s, out g))
            {
                id = g;
                return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    private void Window_OnPreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!TryGetPeggedId(e.Data, out _))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = System.Windows.DragDropEffects.Move;
        _dragInsertIndex = EstimateInsertIndex(e.GetPosition(RootCanvas).X);
        e.Handled = true;
    }

    private void Window_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!TryGetPeggedId(e.Data, out var id)) return;
        var idx = _dragInsertIndex ?? EstimateInsertIndex(e.GetPosition(RootCanvas).X);
        _line.MoveToVisibleIndex(id, idx);
        e.Handled = true;
    }

    private void Window_OnDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        // no-op (trash UI removed)
    }

    private int EstimateInsertIndex(double canvasX)
    {
        var visible = _line.VisibleItems();
        if (visible.Count == 0) return 0;

        // Ignore the card being dragged when computing neighbors.
        var dragId = _line.DraggingId;
        var slots = visible.Where(v => v.Id != dragId).ToList();
        var count = slots.Count + 1; // include the dragged card's target slot
        var layoutWidth = ActualWidth > 1 ? ActualWidth : Width;
        var overflowReserve = _line.OverflowItems().Count > 0 && !_line.PhotosFolded ? 72 : 0;
        var inner = Math.Max(200, layoutWidth - overflowReserve);

        for (var i = 0; i < count; i++)
        {
            var cx = LayoutCenterX(i, count, inner);
            if (canvasX < cx) return i;
        }
        return count - 1;
    }

    private void LayoutRope(bool empty, bool folded, double layoutWidth)
    {
        const double ropeY = 18;
        _ropeY = ropeY;

        if (empty)
        {
            RopeCapL.Visibility = Visibility.Collapsed;
            RopeCapR.Visibility = Visibility.Collapsed;
            RopeKnot.Visibility = Visibility.Visible;
            Canvas.SetLeft(RopeKnot, (layoutWidth - RopeKnot.Width) / 2);
            Canvas.SetTop(RopeKnot, 4);

            Rope.Data = Geometry.Empty;
            RopeShade.Data = Geometry.Empty;
            _ropeSag = 0;
            return;
        }

        RopeKnot.Visibility = Visibility.Collapsed;
        RopeCapL.Visibility = Visibility.Visible;
        RopeCapR.Visibility = Visibility.Visible;

        // Folded: short rope segment on the right; expanded: full span.
        Canvas.SetLeft(RopeCapL, folded ? 4 : 8);
        Canvas.SetTop(RopeCapL, 2);
        Canvas.SetLeft(RopeCapR, Math.Max(folded ? 40 : 50, layoutWidth - (folded ? 44 : 48)));
        Canvas.SetTop(RopeCapR, 2);

        var x0 = folded ? 36 : 40;
        var x1 = Math.Max(folded ? 60 : 80, layoutWidth - (folded ? 36 : 40));
        var sag = ComputeRopeSag(_line.LiveCount, folded);
        _ropeX0 = x0;
        _ropeX1 = x1;
        _ropeSag = sag;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new WpfPoint(x0, ropeY), false, false);
            ctx.QuadraticBezierTo(
                new WpfPoint(layoutWidth / 2, ropeY + sag),
                new WpfPoint(x1, ropeY),
                true, false);
        }
        geo.Freeze();
        Rope.Data = geo;

        var shade = new StreamGeometry();
        using (var ctx = shade.Open())
        {
            ctx.BeginFigure(new WpfPoint(x0, ropeY + 1.6), false, false);
            ctx.QuadraticBezierTo(
                new WpfPoint(layoutWidth / 2, ropeY + sag + 1.5),
                new WpfPoint(x1, ropeY + 1.6),
                true, false);
        }
        shade.Freeze();
        RopeShade.Data = shade;
    }

    /// <summary>Weight-based sag: grows with sqrt(count), capped so it never looks collapsed.</summary>
    private static double ComputeRopeSag(int liveCount, bool folded)
    {
        if (folded) return 8;
        var n = Math.Max(0, liveCount);
        // Stronger weight feel than v1, still capped so the rope doesn't collapse.
        var sag = 14 + 3.5 * Math.Sqrt(n);
        return Math.Clamp(sag, 14, 38);
    }

    /// <summary>Y of the clothesline at canvas X (quadratic bezier through mid sag).</summary>
    public double RopeYAt(double canvasX)
    {
        var span = _ropeX1 - _ropeX0;
        if (span < 1) return _ropeY;
        var t = Math.Clamp((canvasX - _ropeX0) / span, 0, 1);
        var y0 = _ropeY;
        var y1 = _ropeY + _ropeSag;
        var y2 = _ropeY;
        var u = 1 - t;
        return u * u * y0 + 2 * u * t * y1 + t * t * y2;
    }

    /// <summary>Card top so the clothespin bites the rope at centerX.</summary>
    public double CardTopOnRope(double centerX) => RopeYAt(centerX) - RopePinOffset;

    public void ApplyCharmStyle()
    {
        var styles = _line.CurrentCharmStyles;
        var key = ThemeStyles.FormatCharms(styles);
        if (key != _charmGlyphKey)
        {
            RebuildCharmNodes(styles);
            _charmGlyphKey = key;
        }
        SyncCharmSway();
    }

    private void RebuildCharmNodes(IReadOnlyList<CharmStyle> styles)
    {
        StopCharmSway();
        CharmHost.Children.Clear();
        _charmSwings.Clear();
        foreach (var style in styles)
        {
            var node = new Canvas
            {
                Width = 28,
                Height = 42,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            var sway = new RotateTransform(0, 14, 0);
            node.RenderTransform = sway;
            _charmSwings.Add(sway);

            node.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 14, Y1 = 0, X2 = 14, Y2 = 8,
                Stroke = ThemeStyles.Brush(System.Windows.Media.Color.FromRgb(0x88, 0x66, 0x55)),
                StrokeThickness = 1,
                StrokeStartLineCap = PenLineCap.Round
            });
            var glyph = new Canvas { Width = 28, Height = 34 };
            Canvas.SetTop(glyph, 6);
            BuildCharmGlyph(glyph, style);
            node.Children.Add(glyph);
            CharmHost.Children.Add(node);
        }
    }

    private void PlaceCharms(bool empty, bool folded, double layoutWidth)
    {
        ApplyCharmStyle();
        var styles = _line.CurrentCharmStyles;
        var show = !empty && !folded && styles.Count > 0 && layoutWidth > 120;
        if (!show || CharmHost.Children.Count == 0)
        {
            foreach (UIElement child in CharmHost.Children)
                child.Visibility = Visibility.Collapsed;
            StopCharmSway();
            return;
        }

        var leftCap = Canvas.GetLeft(RopeCapL);
        var rightCap = Canvas.GetLeft(RopeCapR);
        if (double.IsNaN(leftCap)) leftCap = 8;
        if (double.IsNaN(rightCap)) rightCap = Math.Max(50, layoutWidth - 48);

        // Split selected charms across both ends (first half left, rest right).
        var n = CharmHost.Children.Count;
        var leftN = (n + 1) / 2;
        const double spacing = 26;

        for (var i = 0; i < n; i++)
        {
            if (CharmHost.Children[i] is not Canvas node) continue;
            node.Visibility = Visibility.Visible;
            double x;
            if (i < leftN)
            {
                x = leftCap + 28 + i * spacing;
            }
            else
            {
                var ri = i - leftN;
                var rightCount = n - leftN;
                x = rightCap - 16 - (rightCount - 1 - ri) * spacing;
            }
            var top = RopeYAt(x + 14) - 4;
            Canvas.SetLeft(node, x);
            Canvas.SetTop(node, top);
        }
        SyncCharmSway();
    }

    private void SyncCharmSway()
    {
        var should = _line.BreezeSwayEnabled
                     && _line.HasCharms
                     && CharmHost.Children.Count > 0
                     && CharmHost.Children.Cast<UIElement>().Any(c => c.Visibility == Visibility.Visible);
        if (should) StartCharmSway();
        else StopCharmSway();
    }

    private void StartCharmSway()
    {
        if (_charmSwayRunning || _charmSwings.Count == 0) return;
        _charmSwayRunning = true;
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        for (var i = 0; i < _charmSwings.Count; i++)
        {
            var amp = 6.0 + (i % 3);
            var dur = 2.2 + i * 0.18;
            var anim = new DoubleAnimation(-amp, amp, TimeSpan.FromSeconds(dur))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease,
                BeginTime = TimeSpan.FromMilliseconds(120 * i)
            };
            _charmSwings[i].BeginAnimation(RotateTransform.AngleProperty, anim);
        }
    }

    private void StopCharmSway()
    {
        if (!_charmSwayRunning && _charmSwings.All(s => s.Angle == 0)) return;
        _charmSwayRunning = false;
        foreach (var sway in _charmSwings)
        {
            sway.BeginAnimation(RotateTransform.AngleProperty, null);
            sway.Angle = 0;
        }
    }

    private static void BuildCharmGlyph(Canvas host, CharmStyle style)
    {
        host.Children.Clear();
        if (style == CharmStyle.None) return;

        SolidColorBrush B(byte r, byte g, byte b) => ThemeStyles.Brush(System.Windows.Media.Color.FromRgb(r, g, b));
        SolidColorBrush Ba(byte a, byte r, byte g, byte b) => ThemeStyles.Brush(System.Windows.Media.Color.FromArgb(a, r, g, b));

        switch (style)
        {
            case CharmStyle.Bear:
            {
                // ears
                host.Children.Add(Oval(4, 4, 8, 8, B(0xC4, 0x8A, 0x4A), B(0x8B, 0x5E, 0x34)));
                host.Children.Add(Oval(16, 4, 8, 8, B(0xC4, 0x8A, 0x4A), B(0x8B, 0x5E, 0x34)));
                host.Children.Add(Oval(6, 6, 4, 4, B(0xE8, 0xB0, 0x80), null));
                host.Children.Add(Oval(18, 6, 4, 4, B(0xE8, 0xB0, 0x80), null));
                // head
                host.Children.Add(Oval(5, 8, 18, 16, B(0xD2, 0xA0, 0x6A), B(0x8B, 0x5E, 0x34)));
                host.Children.Add(Oval(10, 14, 8, 6, B(0xE8, 0xC4, 0x9A), null));
                host.Children.Add(Oval(11, 12, 2.2, 2.2, B(0x3A, 0x28, 0x18), null));
                host.Children.Add(Oval(15.5, 12, 2.2, 2.2, B(0x3A, 0x28, 0x18), null));
                host.Children.Add(Oval(12.5, 15.5, 3, 2.2, B(0x5A, 0x3A, 0x22), null));
                break;
            }
            case CharmStyle.Sock:
            {
                var sock = new System.Windows.Shapes.Path
                {
                    Fill = B(0xE8, 0x6A, 0x7A),
                    Stroke = B(0xA0, 0x3A, 0x4A),
                    StrokeThickness = 1,
                    Data = Geometry.Parse(
                        "M 10,4 L 18,4 L 18,14 L 22,18 L 22,24 L 12,24 L 8,20 L 8,14 Z")
                };
                host.Children.Add(sock);
                var cuff = new System.Windows.Shapes.Rectangle
                {
                    Width = 10, Height = 3, Fill = B(0xF8, 0xF0, 0xE8),
                    Stroke = B(0xA0, 0x3A, 0x4A), StrokeThickness = 0.6
                };
                Canvas.SetLeft(cuff, 9);
                Canvas.SetTop(cuff, 3);
                host.Children.Add(cuff);
                break;
            }
            case CharmStyle.Cloud:
            {
                host.Children.Add(Oval(3, 12, 12, 10, B(0xF0, 0xF4, 0xF8), B(0x8A, 0x9A, 0xAA)));
                host.Children.Add(Oval(12, 10, 14, 12, B(0xF5, 0xF8, 0xFC), B(0x8A, 0x9A, 0xAA)));
                host.Children.Add(Oval(8, 8, 10, 9, Ba(0xEE, 0xFF, 0xFF, 0xFF), null));
                break;
            }
            case CharmStyle.Star:
            {
                var star = new System.Windows.Shapes.Path
                {
                    Fill = B(0xF0, 0xC8, 0x40),
                    Stroke = B(0xC0, 0x8A, 0x20),
                    StrokeThickness = 1,
                    Data = Geometry.Parse(
                        "M 14,4 L 16.5,11 L 24,11.5 L 18,16 L 20,24 L 14,19.5 L 8,24 L 10,16 L 4,11.5 L 11.5,11 Z")
                };
                host.Children.Add(star);
                break;
            }
            case CharmStyle.Heart:
            {
                var heart = new System.Windows.Shapes.Path
                {
                    Fill = B(0xE8, 0x5A, 0x6E),
                    Stroke = B(0xA0, 0x30, 0x48),
                    StrokeThickness = 1,
                    Data = Geometry.Parse(
                        "M 14,22 C 8,16 4,12 4,8.5 C 4,5.5 6.5,4 9,4 C 11,4 12.8,5.2 14,7 C 15.2,5.2 17,4 19,4 C 21.5,4 24,5.5 24,8.5 C 24,12 20,16 14,22 Z")
                };
                host.Children.Add(heart);
                break;
            }
            case CharmStyle.Moon:
            {
                var moon = new System.Windows.Shapes.Path
                {
                    Fill = B(0xF0, 0xD8, 0x60),
                    Stroke = B(0xC0, 0xA0, 0x30),
                    StrokeThickness = 1,
                    Data = Geometry.Parse(
                        "M 18,5 C 10,5 5,11 5,16 C 5,21 10,26 18,26 C 13,24 10,20 10,16 C 10,12 13,7 18,5 Z")
                };
                host.Children.Add(moon);
                break;
            }
        }
    }

    private static System.Windows.Shapes.Ellipse Oval(
        double left, double top, double w, double h,
        System.Windows.Media.Brush fill, System.Windows.Media.Brush? stroke)
    {
        var e = new System.Windows.Shapes.Ellipse
        {
            Width = w,
            Height = h,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = stroke is null ? 0 : 1
        };
        Canvas.SetLeft(e, left);
        Canvas.SetTop(e, top);
        return e;
    }

    private void UpdateFoldButton(double layoutWidth)
    {
        var live = _line.LiveCount;
        if (live == 0)
        {
            FoldButton.Visibility = Visibility.Collapsed;
            return;
        }

        FoldButton.Visibility = Visibility.Visible;
        FoldButtonText.Text = _line.PhotosFolded ? "展开" : "折叠";
        FoldButton.ToolTip = _line.PhotosFolded ? "展开全部截图" : "把绳子上的截图收成一叠";
        // Folded compact chrome: tuck the button under the left cap.
        Canvas.SetLeft(FoldButton, _line.PhotosFolded ? 6 : 56);
        Canvas.SetTop(FoldButton, _line.PhotosFolded ? 36 : 6);
        _ = layoutWidth;
    }

    private void FoldButton_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        PingEmptyIdle();
        RequestToggleFold();
    }

    private void UpdateOverflowBadge(IReadOnlyList<Pegged> overflow, double layoutWidth, bool folded)
    {
        if (overflow.Count == 0)
        {
            OverflowBadge.Visibility = Visibility.Collapsed;
            if (OverflowPopup.IsOpen) OverflowPopup.IsOpen = false;
            return;
        }

        OverflowBadge.Visibility = Visibility.Visible;
        OverflowCountText.Text = folded ? $"{overflow.Count}" : $"+{overflow.Count}";
        OverflowBadge.ToolTip = folded ? "点击展开全部截图" : "点击查看更多截图";
        var newestOverflow = overflow[^1];
        OverflowThumb.Source = newestOverflow.Thumb;

        // Folded compact: center the stack on the short rope; expanded overflow: right side.
        var x = folded
            ? Math.Max(8, (layoutWidth - OverflowBadge.Width) / 2)
            : Math.Max(80, layoutWidth - 108);
        Canvas.SetLeft(OverflowBadge, x);
        Canvas.SetTop(OverflowBadge, 8);
    }

    private void OverflowBadge_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        PingEmptyIdle();

        // Fully folded stack → expand all with scatter animation.
        if (_line.PhotosFolded)
        {
            if (OverflowPopup.IsOpen) OverflowPopup.IsOpen = false;
            RequestToggleFold();
            return;
        }

        if (OverflowPopup.IsOpen)
        {
            OverflowPopup.IsOpen = false;
            return;
        }

        BuildOverflowPopup();
        SetPassThrough(false);
        OverflowPopup.IsOpen = true;
    }

    private void BuildOverflowPopup()
    {
        OverflowPanel.Children.Clear();
        var overflow = _line.OverflowItems().Reverse().ToList(); // newest folded first
        foreach (var item in overflow)
        {
            var tile = new Border
            {
                Width = 88,
                Height = 100,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(5),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xEE, 0xF5, 0xF5, 0xF5)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = item.Id,
                ToolTip = Path.GetFileName(item.Path)
            };
            var img = new System.Windows.Controls.Image
            {
                Source = item.Thumb,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4)
            };
            tile.Child = img;
            tile.MouseLeftButtonUp += OverflowTile_OnClick;
            OverflowPanel.Children.Add(tile);
        }
    }

    private void OverflowTile_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: Guid id }) return;
        OverflowPopup.IsOpen = false;
        // Bring folded shot onto the visible rope (newest slot).
        _line.BringToFront(id);
    }

    private void OverflowPopup_OnOpened(object? sender, EventArgs e)
    {
        _overflowPopupOpen = true;
        SetPassThrough(false);
    }

    private void OverflowPopup_OnClosed(object? sender, EventArgs e)
    {
        _overflowPopupOpen = false;
        SetPassThrough(true);
    }

    private static double LayoutCenterX(int index, int count, double width)
    {
        if (count <= 0) return width / 2;
        if (count == 1) return width / 2;

        var margin = Math.Max(80, width * 0.08);
        var usable = Math.Max(MinSpacing, width - margin * 2);
        var spacing = Math.Min(160, usable / Math.Max(1, count - 1));
        var total = spacing * (count - 1);
        var start = (width - total) / 2;
        return start + index * spacing;
    }
}

internal static class RectExtensions
{
    public static WpfRect InflateCopy(this WpfRect r, double amount)
    {
        if (r.IsEmpty) return r;
        return new WpfRect(r.X - amount, r.Y - amount, r.Width + amount * 2, r.Height + amount * 2);
    }
}
