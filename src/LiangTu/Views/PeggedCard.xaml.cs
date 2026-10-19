using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LiangTu.Models;
using LiangTu.Services;

namespace LiangTu.Views;

public partial class PeggedCard : System.Windows.Controls.UserControl
{
    private readonly Line _line;
    private readonly Pegged _item;
    private readonly LineOverlayWindow _overlay;
    private readonly double _swayAmp;
    private readonly double _swayHalfPeriod;
    private readonly double _tiltAmp;
    private readonly double _phaseDelayMs;
    private System.Windows.Point _pressPoint;
    private DateTime _pressAt;
    private DispatcherTimer? _longPress;
    private DispatcherTimer? _pendingClick;
    private DispatcherTimer? _gustDelay;
    private bool _dragStarted;
    private bool _suppressClick;
    private bool _swayRunning;
    private bool _localDragging;
    private bool _oleDragging;
    private bool _pinOpen;
    private double _homeLeft;
    private double _homeTop;
    private double _grabOffsetX;
    private double _grabOffsetY;

    public Pegged Item => _item;
    public bool IsLocalDragging => _localDragging;

    public PeggedCard(Line line, Pegged item, LineOverlayWindow overlay)
    {
        InitializeComponent();
        _line = line;
        _item = item;
        _overlay = overlay;

        _swayAmp = 2.2 + Random.Shared.NextDouble() * 5.5;
        _swayHalfPeriod = 0.55 + Random.Shared.NextDouble() * 1.15;
        _tiltAmp = 1.0 + Random.Shared.NextDouble() * 3.2;
        _phaseDelayMs = Random.Shared.Next(0, 2400);

        RefreshVisual();
        SetPinOpen(false, animate: false);
        _line.ThemeChanged += () => Dispatcher.BeginInvoke(() =>
        {
            ApplyPinStyle(_line.CurrentPinStyle);
            ApplyPhotoStyle(_line.CurrentPhotoStyle);
            SetPinOpen(_pinOpen, animate: false);
        });

        PreviewMouseLeftButtonDown += OnDown;
        PreviewMouseLeftButtonUp += OnUp;
        PreviewMouseMove += OnMove;
        LostMouseCapture += (_, _) =>
        {
            if (_localDragging && !_oleDragging)
                EndLocalDrag(commit: false);
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && IsLoaded) StartIdleSway();
            else StopSway();
        };

        _line.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Line.CopiedId))
                CopiedBadge.Visibility = _line.CopiedId == _item.Id ? Visibility.Visible : Visibility.Collapsed;
            if (e.PropertyName == nameof(Line.Gust))
                ScheduleWindGust();
            if (e.PropertyName == nameof(Line.BreezeSwayEnabled))
            {
                if (_line.BreezeSwayEnabled) StartIdleSway();
                else StopSway();
            }
            if (e.PropertyName == nameof(Line.DraggingId))
            {
                if (_line.DraggingId == _item.Id) StopSway();
                else if (_line.DraggingId is null && IsVisible && !_localDragging) StartIdleSway();
            }
            if (e.PropertyName == nameof(Line.PhotosFolded))
            {
                if (_line.PhotosFolded) StopSway();
                else if (IsVisible) StartIdleSway();
            }
        };

        Loaded += (_, _) => StartIdleSway();
        Unloaded += (_, _) => StopSway();
    }

    public void RefreshVisual()
    {
        ThumbImage.Source = _item.Thumb;
        TiltTransform.Angle = _item.Tilt;
        NameLabel.Text = Path.GetFileName(_item.Path);
        PinBadge.Visibility = _item.Pinned ? Visibility.Visible : Visibility.Collapsed;
        if (PinMenu is not null)
            PinMenu.Header = _item.Pinned ? "取消固定" : "固定在前排";
        ApplyPinStyle(_line.CurrentPinStyle);
        ApplyPhotoStyle(_line.CurrentPhotoStyle);
    }

    public void ApplyPhotoStyle(PhotoStyle style)
    {
        var p = ThemeStyles.Photo(style);
        Frame.Background = ThemeStyles.Brush(p.Background);
        Frame.BorderThickness = new Thickness(p.BorderThickness);
        Frame.CornerRadius = new CornerRadius(p.CornerRadius);
        Frame.Padding = new Thickness(p.Padding, p.Padding, p.Padding, p.BottomPad);
        NameLabel.Foreground = ThemeStyles.Brush(p.NameForeground);
        NameLabel.Margin = new Thickness(2, 0, 2, Math.Max(1, p.BottomPad * 0.25));

        var tapeVis = p.ShowTape ? Visibility.Visible : Visibility.Collapsed;
        TapeTL.Visibility = tapeVis;
        TapeTR.Visibility = tapeVis;
        if (p.ShowTape && p.TapeColor is { } tape)
        {
            var brush = ThemeStyles.Brush(tape);
            TapeTL.Background = brush;
            TapeTR.Background = brush;
        }

        // Polaroid: softer shadow; wood: a bit deeper
        if (FrameShadow is not null)
        {
            FrameShadow.BlurRadius = style == PhotoStyle.Polaroid ? 10 : style == PhotoStyle.Wood ? 16 : 14;
            FrameShadow.Opacity = style == PhotoStyle.Tape ? 0.28 : 0.4;
        }

        // Pinned gold border overrides style border; otherwise use style border.
        Frame.BorderBrush = _item.Pinned
            ? ThemeStyles.Brush(System.Windows.Media.Color.FromArgb(0xEE, 0xC9, 0xA2, 0x27))
            : ThemeStyles.Brush(p.Border);
    }

    public void ApplyPinStyle(PinStyle style)
    {
        var p = ThemeStyles.Pin(style);
        var geo = ThemeStyles.PinGeometry(p.Shape);
        PinRoot.Width = geo.CanvasWidth;
        PinRoot.Height = geo.CanvasHeight;
        Frame.Margin = new Thickness(8, geo.FrameTopMargin, 8, 8);

        PegClassicGroup.Visibility = p.Shape == PinShape.Classic ? Visibility.Visible : Visibility.Collapsed;
        SmallClipGroup.Visibility = p.Shape == PinShape.Flat ? Visibility.Visible : Visibility.Collapsed;
        BinderGroup.Visibility = p.Shape == PinShape.Wide ? Visibility.Visible : Visibility.Collapsed;

        if (p.Shape != PinShape.Flat)
        {
            SmallClipScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            SmallClipScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            SmallClipScale.ScaleX = 1;
            SmallClipScale.ScaleY = 1;
        }
        if (p.Shape != PinShape.Wide)
        {
            BinderHandleLRot.BeginAnimation(RotateTransform.AngleProperty, null);
            BinderHandleRRot.BeginAnimation(RotateTransform.AngleProperty, null);
            BinderHandleLRot.Angle = 0;
            BinderHandleRRot.Angle = 0;
        }
        if (p.Shape != PinShape.Classic)
        {
            JawLRot.BeginAnimation(RotateTransform.AngleProperty, null);
            JawRRot.BeginAnimation(RotateTransform.AngleProperty, null);
            JawLRot.Angle = 0;
            JawRRot.Angle = 0;
        }

        if (p.Shape == PinShape.Classic)
        {
            JawL.Data = geo.JawLeft;
            JawR.Data = geo.JawRight;
            JawHighlight.Data = geo.Highlight;
            JawLRot.CenterX = geo.PivotLX;
            JawLRot.CenterY = geo.PivotLY;
            JawRRot.CenterX = geo.PivotRX;
            JawRRot.CenterY = geo.PivotRY;
            Canvas.SetLeft(SpringOuter, geo.SpringLeft);
            Canvas.SetTop(SpringOuter, geo.SpringTop);
            Canvas.SetLeft(SpringInner, geo.SpringLeft + 2);
            Canvas.SetTop(SpringInner, geo.SpringTop + 2);
            JawL.Fill = ThemeStyles.Brush(p.JawLeft);
            JawL.Stroke = ThemeStyles.Brush(p.JawStroke);
            JawR.Fill = ThemeStyles.Brush(p.JawRight);
            JawR.Stroke = ThemeStyles.Brush(p.JawStroke);
            SpringOuter.Fill = ThemeStyles.Brush(p.SpringOuter);
            SpringOuter.Stroke = ThemeStyles.Brush(p.JawStroke);
            SpringInner.Fill = ThemeStyles.Brush(p.SpringInner);
            SpringInner.Stroke = ThemeStyles.Brush(p.SpringOuter);
            JawHighlight.Fill = ThemeStyles.Brush(p.Highlight);
        }
        else if (p.Shape == PinShape.Flat)
        {
            var hole = p.Accent ?? p.SpringOuter;
            var strip = p.Strip ?? p.Highlight;
            SmallBody.Fill = ThemeStyles.Brush(p.JawLeft);
            SmallBody.Stroke = ThemeStyles.Brush(p.JawStroke);
            SmallHole.Fill = ThemeStyles.Brush(hole);
            SmallHole.Stroke = ThemeStyles.Brush(p.JawStroke);
            SmallStrip.Fill = ThemeStyles.Brush(strip);
            SmallStrip.Stroke = ThemeStyles.Brush(p.JawStroke);
        }
        else
        {
            var shade = p.Shade ?? p.JawRight;
            var handle = p.Handle ?? p.SpringOuter;
            BinderBody.Fill = ThemeStyles.Brush(p.JawLeft);
            BinderBody.Stroke = ThemeStyles.Brush(p.JawStroke);
            BinderBodyShade.Fill = ThemeStyles.Brush(shade);
            BinderHighlight.Fill = ThemeStyles.Brush(p.Highlight);
            BinderHandleL.Stroke = ThemeStyles.Brush(handle);
            BinderHandleR.Stroke = ThemeStyles.Brush(handle);
            var hingeFill = ThemeStyles.Brush(shade);
            var hingeStroke = ThemeStyles.Brush(p.JawStroke);
            BinderHinge1.Fill = hingeFill;
            BinderHinge1.Stroke = hingeStroke;
            BinderHinge2.Fill = hingeFill;
            BinderHinge2.Stroke = hingeStroke;
            BinderHinge3.Fill = hingeFill;
            BinderHinge3.Stroke = hingeStroke;
            BinderHandleLRot.CenterX = geo.PivotLX;
            BinderHandleLRot.CenterY = geo.PivotLY;
            BinderHandleRRot.CenterX = geo.PivotRX;
            BinderHandleRRot.CenterY = geo.PivotRY;
        }
    }

    public void SetScaleImmediate(double scale)
    {
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CardScale.ScaleX = scale;
        CardScale.ScaleY = scale;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsDescendantOf(d, PinRoot))
            return;

        if (e.ClickCount >= 2)
        {
            CancelPendingClick();
            _longPress?.Stop();
            _suppressClick = true;
            _dragStarted = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            _line.PressedId = null;
            _line.Open(_item.Id);
            AppLog.Info($"Double-click open {_item.Path}");
            e.Handled = true;
            return;
        }

        Focus();
        _pressPoint = e.GetPosition(this);
        _pressAt = DateTime.Now;
        _dragStarted = false;
        _suppressClick = false;
        _line.PressedId = _item.Id;
        CaptureMouse();

        _longPress?.Stop();
        _longPress = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _longPress.Tick += (_, _) =>
        {
            _longPress.Stop();
            if (!_dragStarted && !_localDragging && IsMouseCaptured)
            {
                CancelPendingClick();
                _suppressClick = true;
                _line.Markup(_item.Id);
                ReleaseMouseCapture();
                _line.PressedId = null;
            }
        };
        _longPress.Start();
        e.Handled = true;
    }

    private void OnMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        if (_localDragging)
        {
            UpdateLocalDrag(e);
            return;
        }

        if (_dragStarted || _oleDragging) return;
        if (!IsMouseCaptured) return;

        var p = e.GetPosition(this);
        if ((p - _pressPoint).Length < 6) return;

        _longPress?.Stop();
        CancelPendingClick();
        _dragStarted = true;
        _suppressClick = true;
        BeginLocalDrag(e);
        e.Handled = true;
    }

    private void BeginLocalDrag(System.Windows.Input.MouseEventArgs e)
    {
        _localDragging = true;
        _homeLeft = Canvas.GetLeft(this);
        _homeTop = Canvas.GetTop(this);
        if (double.IsNaN(_homeLeft)) _homeLeft = 0;
        if (double.IsNaN(_homeTop)) _homeTop = 10;

        var canvasPos = e.GetPosition(_overlay.CardsHostPublic);
        _grabOffsetX = canvasPos.X - _homeLeft;
        _grabOffsetY = canvasPos.Y - _homeTop;

        StopSway();
        System.Windows.Controls.Panel.SetZIndex(this, 200);
        _line.DraggingId = _item.Id;

        // Stay clipped: horizontal slide = reorder. Pull away = export only.
        SetPinOpen(false, animate: false);
        SetDragHint("水平拖动换位");
        Opacity = 1;
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CardScale.ScaleX = 1;
        CardScale.ScaleY = 1;

        AppLog.Info($"Local drag begin {_item.Path}");
        UpdateLocalDrag(e);
    }

    private void UpdateLocalDrag(System.Windows.Input.MouseEventArgs e)
    {
        var host = _overlay.CardsHostPublic;
        var canvasPos = e.GetPosition(host);
        var winPos = e.GetPosition(_overlay);
        var winH = _overlay.ActualHeight > 1 ? _overlay.ActualHeight : 220;
        var winW = _overlay.ActualWidth > 1 ? _overlay.ActualWidth : 800;

        // Leave the overlay → OLE file drag (企微 / 桌面 / 文件夹).
        if (winPos.X < -12 || winPos.Y < -12
            || winPos.X > winW + 12 || winPos.Y > winH + 12)
        {
            BeginOleExport();
            return;
        }

        var freeTop = canvasPos.Y - _grabOffsetY;
        // Pull down / unpin → only for exporting, never for reorder-by-reclipping.
        if (!_overlay.IsNearRope(freeTop) && freeTop >= 88)
        {
            SetPinOpen(true, animate: true);
            BeginOleExport();
            return;
        }

        // Horizontal reorder only — Y locked to the rope.
        SetPinOpen(false, animate: false);
        SetDragHint("水平拖动换位");

        var left = canvasPos.X - _grabOffsetX;
        left = Math.Clamp(left, -40, Math.Max(40, winW - ActualWidth + 40));

        BeginAnimation(Canvas.LeftProperty, null);
        BeginAnimation(Canvas.TopProperty, null);
        Canvas.SetLeft(this, left);
        var centerX = left + ActualWidth / 2;
        Canvas.SetTop(this, _overlay.CardTopOnRope(centerX));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        _longPress?.Stop();

        if (_localDragging)
        {
            EndLocalDrag(commit: true);
            e.Handled = true;
            return;
        }

        if (IsMouseCaptured) ReleaseMouseCapture();
        _line.PressedId = null;

        if (!_dragStarted && !_suppressClick && e.ClickCount == 1
            && (DateTime.Now - _pressAt).TotalMilliseconds < 450)
        {
            ScheduleCopy();
        }

        e.Handled = true;
    }

    private void EndLocalDrag(bool commit)
    {
        if (!_localDragging) return;
        _localDragging = false;
        SetDragHint(null);

        var left = Canvas.GetLeft(this);
        var top = Canvas.GetTop(this);
        if (double.IsNaN(left)) left = _homeLeft;
        if (double.IsNaN(top)) top = _homeTop;

        if (IsMouseCaptured) ReleaseMouseCapture();

        var w = ActualWidth > 0 ? ActualWidth : 148;

        _line.DraggingId = null;
        _line.PressedId = null;
        System.Windows.Controls.Panel.SetZIndex(this, 0);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CardScale.ScaleX = 1;
        CardScale.ScaleY = 1;
        _dragStarted = false;
        SetPinOpen(false, animate: true);

        // Local drag only happens while still clipped on the rope → horizontal reorder.
        if (commit)
        {
            var idx = _overlay.EstimateInsertIndexPublic(left + w / 2);
            _line.MoveToVisibleIndex(_item.Id, idx);
            AppLog.Info($"Local drag → reorder idx={idx}");
        }
        else
        {
            AnimateHome();
            AppLog.Info("Local drag → snap home");
        }

        _overlay.OnLocalCardDragEnded();
    }

    private void AnimateHome()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(220);
        BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(_homeLeft, duration) { EasingFunction = ease });
        BeginAnimation(Canvas.TopProperty, new DoubleAnimation(_homeTop, duration) { EasingFunction = ease });
    }

    private void BeginOleExport()
    {
        if (_oleDragging || !File.Exists(_item.Path)) return;
        _oleDragging = true;
        _localDragging = false;
        SetDragHint(null);
        SetPinOpen(true, animate: true);

        if (IsMouseCaptured) ReleaseMouseCapture();

        // Dim ghost on rope; real drag is OLE.
        Opacity = 0.35;
        System.Windows.Controls.Panel.SetZIndex(this, 0);

        try
        {
            var data = new System.Windows.DataObject();
            data.SetData(LineOverlayWindow.PeggedIdFormat, _item.Id);
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { _item.Path });

            var effects = System.Windows.DragDrop.DoDragDrop(
                this,
                data,
                System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move);

            AppLog.Info($"OLE drag ended effect={effects} path={_item.Path}");

            if (effects == System.Windows.DragDropEffects.Move && File.Exists(_item.Path)
                && _line.Items.Any(i => i.Id == _item.Id && !i.Falling))
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    if (!File.Exists(_item.Path))
                        _line.Drop(_item.Id, quietly: true);
                    else
                        _line.Prune();
                };
                t.Start();
            }
            else
            {
                _line.Prune();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"OLE drag failed: {ex.Message}");
        }
        finally
        {
            _oleDragging = false;
            _dragStarted = false;
            Opacity = 1;
            SetPinOpen(false, animate: true);
            _line.DraggingId = null;
            _line.PressedId = null;
            CardScale.ScaleX = 1;
            CardScale.ScaleY = 1;
            _overlay.OnLocalCardDragEnded();
        }
    }

    private void SetPinOpen(bool open, bool animate)
    {
        _pinOpen = open;
        var palette = ThemeStyles.Pin(_line.CurrentPinStyle);
        var geo = ThemeStyles.PinGeometry(palette.Shape);
        var openAngle = geo.OpenAngle;
        var lift = open ? (palette.Shape == PinShape.Wide ? -5.0 : -7.0) : 0.0;
        var ms = animate ? 160 : 0;
        var ease = open
            ? (IEasingFunction)new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }
            : new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(ms);

        void ClearAnim()
        {
            JawLRot.BeginAnimation(RotateTransform.AngleProperty, null);
            JawRRot.BeginAnimation(RotateTransform.AngleProperty, null);
            BinderHandleLRot.BeginAnimation(RotateTransform.AngleProperty, null);
            BinderHandleRRot.BeginAnimation(RotateTransform.AngleProperty, null);
            SmallClipScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            SmallClipScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            PinLift.BeginAnimation(TranslateTransform.YProperty, null);
        }

        if (palette.Shape == PinShape.Classic)
        {
            var jawL = open ? -openAngle : 0.0;
            var jawR = open ? openAngle : 0.0;
            if (!animate)
            {
                ClearAnim();
                JawLRot.Angle = jawL;
                JawRRot.Angle = jawR;
                BinderHandleLRot.Angle = 0;
                BinderHandleRRot.Angle = 0;
                SmallClipScale.ScaleX = 1;
                SmallClipScale.ScaleY = 1;
                PinLift.Y = lift;
                return;
            }
            JawLRot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(jawL, dur) { EasingFunction = ease });
            JawRRot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(jawR, dur) { EasingFunction = ease });
            PinLift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(lift, dur) { EasingFunction = ease });
            return;
        }

        if (palette.Shape == PinShape.Flat)
        {
            // 小夹子：上提 + 略张宽，模拟松口夹取
            var sx = open ? 1.12 : 1.0;
            var sy = open ? 0.92 : 1.0;
            if (!animate)
            {
                ClearAnim();
                JawLRot.Angle = 0;
                JawRRot.Angle = 0;
                BinderHandleLRot.Angle = 0;
                BinderHandleRRot.Angle = 0;
                SmallClipScale.ScaleX = sx;
                SmallClipScale.ScaleY = sy;
                PinLift.Y = lift;
                return;
            }
            SmallClipScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(sx, dur) { EasingFunction = ease });
            SmallClipScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(sy, dur) { EasingFunction = ease });
            PinLift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(lift, dur) { EasingFunction = ease });
            return;
        }

        // 长尾夹：钢丝柄外张 + 上提
        var hL = open ? -openAngle : 0.0;
        var hR = open ? openAngle : 0.0;
        if (!animate)
        {
            ClearAnim();
            JawLRot.Angle = 0;
            JawRRot.Angle = 0;
            BinderHandleLRot.Angle = hL;
            BinderHandleRRot.Angle = hR;
            SmallClipScale.ScaleX = 1;
            SmallClipScale.ScaleY = 1;
            PinLift.Y = lift;
            return;
        }
        BinderHandleLRot.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(hL, dur) { EasingFunction = ease });
        BinderHandleRRot.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(hR, dur) { EasingFunction = ease });
        PinLift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(lift, dur) { EasingFunction = ease });
    }

    private void SetDragHint(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            DragHint.Visibility = Visibility.Collapsed;
            return;
        }
        DragHintText.Text = text;
        DragHint.Background = new SolidColorBrush(
            System.Windows.Media.Color.FromArgb(0xCC, 0x2E, 0x5A, 0x8C));
        DragHint.Visibility = Visibility.Visible;
    }

    private void ScheduleCopy()
    {
        CancelPendingClick();
        var delay = TimeSpan.FromMilliseconds(Math.Max(280, System.Windows.Forms.SystemInformation.DoubleClickTime));
        _pendingClick = new DispatcherTimer { Interval = delay };
        _pendingClick.Tick += (_, _) =>
        {
            _pendingClick.Stop();
            _pendingClick = null;
            if (_dragStarted || _suppressClick || _localDragging) return;
            _line.Copy(_item.Id);
            AppLog.Info($"Click copy {_item.Path}");
        };
        _pendingClick.Start();
    }

    private void CancelPendingClick()
    {
        if (_pendingClick is null) return;
        _pendingClick.Stop();
        _pendingClick = null;
    }

    private void PinRoot_OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CancelPendingClick();
        _suppressClick = true;
        _longPress?.Stop();
        _dragStarted = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        _line.PressedId = null;
        _line.Discard(_item.Id);
    }

    private void MenuCopy_OnClick(object sender, RoutedEventArgs e) => _line.Copy(_item.Id);
    private void MenuOpen_OnClick(object sender, RoutedEventArgs e) => _line.Open(_item.Id);
    private void MenuEdit_OnClick(object sender, RoutedEventArgs e) => _line.Markup(_item.Id);
    private void MenuReveal_OnClick(object sender, RoutedEventArgs e) => _line.RevealInExplorer(_item.Id);
    private void MenuTakeDown_OnClick(object sender, RoutedEventArgs e) => _line.Drop(_item.Id);
    private void MenuTrash_OnClick(object sender, RoutedEventArgs e) => _line.Trash(_item.Id);

    private void MenuPin_OnClick(object sender, RoutedEventArgs e)
    {
        _line.SetPinned(_item.Id, !_item.Pinned);
        RefreshVisual();
    }

    private void MenuRename_OnClick(object sender, RoutedEventArgs e)
    {
        if (_line.Rename(_item.Id))
            RefreshVisual();
    }

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject parent)
    {
        while (child is not null)
        {
            if (ReferenceEquals(child, parent)) return true;
            child = VisualTreeHelper.GetParent(child);
        }
        return false;
    }

    private void StartIdleSway()
    {
        if (!_line.BreezeSwayEnabled) { StopSway(); return; }
        if (_item.Falling || _line.PhotosFolded || _line.DraggingId == _item.Id || _localDragging) return;
        if (Visibility != Visibility.Visible || Opacity < 0.2) return;
        if (_swayRunning) return;
        _swayRunning = true;

        var sine = new SineEase { EasingMode = EasingMode.EaseInOut };
        var half = TimeSpan.FromSeconds(_swayHalfPeriod);
        var begin = TimeSpan.FromMilliseconds(_phaseDelayMs);

        var xAnim = new DoubleAnimation(-_swayAmp, _swayAmp, half)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = sine,
            BeginTime = begin
        };
        SwingTransform.BeginAnimation(TranslateTransform.XProperty, xAnim);

        var baseTilt = _item.Tilt;
        var tiltAnim = new DoubleAnimation(baseTilt - _tiltAmp, baseTilt + _tiltAmp, half)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = sine,
            BeginTime = begin + TimeSpan.FromMilliseconds(_phaseDelayMs * 0.37)
        };
        TiltTransform.BeginAnimation(RotateTransform.AngleProperty, tiltAnim);
    }

    private void StopSway()
    {
        _swayRunning = false;
        _gustDelay?.Stop();
        SwingTransform.BeginAnimation(TranslateTransform.XProperty, null);
        TiltTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        SwingTransform.X = 0;
        TiltTransform.Angle = _item.Tilt;
    }

    private void ScheduleWindGust()
    {
        if (!_line.BreezeSwayEnabled) return;
        if (_item.Falling || _line.PhotosFolded || Visibility != Visibility.Visible) return;
        if (_line.DraggingId == _item.Id || _localDragging) return;

        _gustDelay?.Stop();
        var delay = Math.Abs(_item.Id.GetHashCode()) % 900;
        _gustDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
        _gustDelay.Tick += (_, _) =>
        {
            _gustDelay.Stop();
            PlayWindGust();
        };
        _gustDelay.Start();
    }

    private void PlayWindGust()
    {
        if (_item.Falling || Visibility != Visibility.Visible || _localDragging) return;

        var gustAmp = _swayAmp * (1.6 + Random.Shared.NextDouble() * 0.9);
        var gustTilt = _tiltAmp * (1.5 + Random.Shared.NextDouble() * 0.8);
        var duration = TimeSpan.FromSeconds(0.9 + Random.Shared.NextDouble() * 0.7);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var dir = Random.Shared.Next(2) == 0 ? 1 : -1;

        _swayRunning = false;
        var xGust = new DoubleAnimationUsingKeyFrames { Duration = duration };
        xGust.KeyFrames.Add(new EasingDoubleKeyFrame(dir * gustAmp, KeyTime.FromPercent(0.28), ease));
        xGust.KeyFrames.Add(new EasingDoubleKeyFrame(-dir * gustAmp * 0.65, KeyTime.FromPercent(0.62), ease));
        xGust.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1.0), ease));
        xGust.Completed += (_, _) => StartIdleSway();
        SwingTransform.BeginAnimation(TranslateTransform.XProperty, xGust);

        var baseTilt = _item.Tilt;
        var tGust = new DoubleAnimationUsingKeyFrames { Duration = duration };
        tGust.KeyFrames.Add(new EasingDoubleKeyFrame(baseTilt + dir * gustTilt, KeyTime.FromPercent(0.3), ease));
        tGust.KeyFrames.Add(new EasingDoubleKeyFrame(baseTilt - dir * gustTilt * 0.55, KeyTime.FromPercent(0.65), ease));
        tGust.KeyFrames.Add(new EasingDoubleKeyFrame(baseTilt, KeyTime.FromPercent(1.0), ease));
        TiltTransform.BeginAnimation(RotateTransform.AngleProperty, tGust);
    }

    public void PlayDismiss(Action? onCompleted = null)
    {
        StopSway();
        IsHitTestVisible = false;
        PinRoot.IsHitTestVisible = false;
        SetPinOpen(true, animate: true);

        var duration = TimeSpan.FromMilliseconds(860);
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
        var sine = new SineEase { EasingMode = EasingMode.EaseInOut };
        var baseTilt = TiltTransform.Angle;
        var dir = Random.Shared.Next(2) == 0 ? 1.0 : -1.0;

        var fade = new DoubleAnimation(0, duration) { EasingFunction = easeIn };
        var fall = new DoubleAnimation(0, 168, duration) { EasingFunction = easeIn };
        var scale = new DoubleAnimation(1, 0.78, duration) { EasingFunction = easeIn };

        // Leaf-like horizontal sway while falling.
        var sway = new DoubleAnimationUsingKeyFrames { Duration = duration };
        sway.KeyFrames.Add(new EasingDoubleKeyFrame(dir * 18, KeyTime.FromPercent(0.22), sine));
        sway.KeyFrames.Add(new EasingDoubleKeyFrame(dir * -22, KeyTime.FromPercent(0.48), sine));
        sway.KeyFrames.Add(new EasingDoubleKeyFrame(dir * 14, KeyTime.FromPercent(0.72), sine));
        sway.KeyFrames.Add(new EasingDoubleKeyFrame(dir * -8, KeyTime.FromPercent(1.0), easeIn));

        var tilt = new DoubleAnimationUsingKeyFrames { Duration = duration };
        tilt.KeyFrames.Add(new EasingDoubleKeyFrame(baseTilt + dir * 16, KeyTime.FromPercent(0.25), sine));
        tilt.KeyFrames.Add(new EasingDoubleKeyFrame(baseTilt - dir * 22, KeyTime.FromPercent(0.55), sine));
        tilt.KeyFrames.Add(new EasingDoubleKeyFrame(baseTilt + dir * 28, KeyTime.FromPercent(1.0), easeIn));

        fade.Completed += (_, _) => onCompleted?.Invoke();
        BeginAnimation(OpacityProperty, fade);
        DismissTransform.BeginAnimation(TranslateTransform.YProperty, fall);
        DismissTransform.BeginAnimation(TranslateTransform.XProperty, sway);
        TiltTransform.BeginAnimation(RotateTransform.AngleProperty, tilt);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    public void AnimateTo(double left, double top, double opacity, double scale, int delayMs, int durationMs, Action? onCompleted = null)
    {
        StopSway();
        IsHitTestVisible = false;
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var begin = TimeSpan.FromMilliseconds(delayMs);

        var leftAnim = new DoubleAnimation(left, duration) { BeginTime = begin, EasingFunction = ease };
        var topAnim = new DoubleAnimation(top, duration) { BeginTime = begin, EasingFunction = ease };
        var opAnim = new DoubleAnimation(opacity, duration) { BeginTime = begin, EasingFunction = ease };
        var scAnim = new DoubleAnimation(scale, duration) { BeginTime = begin, EasingFunction = ease };

        if (onCompleted is not null)
            opAnim.Completed += (_, _) => onCompleted();

        BeginAnimation(Canvas.LeftProperty, leftAnim);
        BeginAnimation(Canvas.TopProperty, topAnim);
        BeginAnimation(OpacityProperty, opAnim);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scAnim);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scAnim);
    }

    public void ResetMotionChrome()
    {
        if (_localDragging || _oleDragging) return;
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(Canvas.LeftProperty, null);
        BeginAnimation(Canvas.TopProperty, null);
        Opacity = 1;
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CardScale.ScaleX = 1;
        CardScale.ScaleY = 1;
        DismissTransform.BeginAnimation(TranslateTransform.YProperty, null);
        DismissTransform.BeginAnimation(TranslateTransform.XProperty, null);
        DismissTransform.Y = 0;
        DismissTransform.X = 0;
        PinRoot.IsHitTestVisible = true;
        SetPinOpen(false, animate: false);
        SetDragHint(null);
        IsHitTestVisible = true;
        StartIdleSway();
    }

    public void UpdateHitRect(UIElement relativeTo)
    {
        try
        {
            var tl = TranslatePoint(new WpfPoint(0, 0), relativeTo);
            _line.HitRects[_item.Id] = new WpfRect(tl.X, tl.Y, ActualWidth, ActualHeight);
        }
        catch
        {
            // not in tree yet
        }
    }
}
