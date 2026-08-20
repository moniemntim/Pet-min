using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using PetNativeWindow = ShuBingDesktopPet.Services.NativeWindow;

namespace ShuBingDesktopPet;

public partial class ObsCaptureWindow : Window
{
    private readonly Random _random = new();
    private readonly DispatcherTimer _tickTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly Dictionary<int, AnimationRowSpec> _rows = new();
    private readonly SpriteSheet _sprite;
    private readonly WindowInteropHelper _interop;
    private readonly AnimationSequencer _sequencer = new();
    private readonly SpriteFrameCache? _frameCache;
    private PetSettings _settings;

    private const double SafeMarginPx = 12.0;
    private const double MinCanvasWidthPx = 1.0;
    private const double MinCanvasHeightPx = 1.0;

    private double _frameWidth = 192;
    private double _frameHeight = 208;
    private int _frameWidthPx;
    private int _frameHeightPx;
    private int _canvasWidthPx;
    private int _canvasHeightPx;
    private double _roamSpeedPxPerSecond;
    private bool _isRoaming;
    private double _roamStopSeconds;
    private double _petXpx;
    private double _petYpx;
    private double _roamTargetX;
    private double _roamTargetY;
    private Vector _roamDirection;
    private bool _isSourceInitialized;
    private bool _disposed;

    private int _cachedRenderedRow = -1;
    private int _cachedRenderedCol = -1;
    private BitmapSource? _cachedRenderedFrame;

    public ObsCaptureWindow(SpriteSheet sprite, SpriteFrameCache? frameCache, PetSettings settings)
    {
        InitializeComponent();

        _sprite = sprite;
        _frameCache = frameCache;
        _interop = new WindowInteropHelper(this);
        _settings = settings;

        InitializeAnimationRows();
        if (_sprite.IsValid)
        {
            _frameWidth = _sprite.FrameWidth;
            _frameHeight = _sprite.FrameHeight;
        }

        ApplySettings(_settings);

        SourceInitialized += OnSourceInitialized;
        DpiChanged += OnDpiChanged;
    }

    public void ApplySettings(PetSettings settings)
    {
        _settings = settings;
        _settings = _settings with
        {
            Scale = PetScaleGeometry.ClampScale(_settings.Scale)
        };

        var (canvasWidth, canvasHeight) = ObsCaptureGeometry.NormalizeResolution(_settings.ObsCanvasWidth, _settings.ObsCanvasHeight);
        _canvasWidthPx = Math.Max((int)MinCanvasWidthPx, canvasWidth);
        _canvasHeightPx = Math.Max((int)MinCanvasHeightPx, canvasHeight);

        _settings = _settings with
        {
            ObsCanvasWidth = _canvasWidthPx,
            ObsCanvasHeight = _canvasHeightPx
        };

        ApplyFrameSize();
        ApplyRoamingSpeed();
        ReinitializePetPosition();

        if (_isSourceInitialized)
        {
            ApplyCanvasSize();
            UpdatePhysicalPosition();
            EnsureTickLoop();
        }
    }

    private void InitializeAnimationRows()
    {
        for (var i = 0; i < 11; i++)
        {
            _rows[i] = AnimationPlanFactory.RowSpec(i);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _isSourceInitialized = true;
        PetNativeWindow.EnsureLayered(_interop.Handle);
        PetNativeWindow.SetClickThrough(_interop.Handle, true);
        ApplyCanvasSize();
        ApplyFrameSize();
        ReinitializePetPosition();
        UpdatePhysicalPosition();

        EnsureTickLoop();
    }

    private void OnDpiChanged(object? sender, System.Windows.DpiChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnDpiChanged(sender, e));
            return;
        }

        ApplyCanvasSize();
        ApplyFrameSize();
        UpdatePhysicalPosition();
    }

    private void EnsureTickLoop()
    {
        if (_tickTimer.IsEnabled)
        {
            return;
        }

        _tickTimer.Tick += OnTick;
        _tickTimer.Start();
        _clock.Start();
    }

    private void ApplyCanvasSize()
    {
        if (_interop.Handle == IntPtr.Zero)
        {
            return;
        }

        if (_canvasWidthPx <= 0 || _canvasHeightPx <= 0)
        {
            return;
        }

        Width = PxToDipX(_canvasWidthPx);
        Height = PxToDipY(_canvasHeightPx);
        CaptureCanvas.Width = Width;
        CaptureCanvas.Height = Height;
        PetNativeWindow.SetWindowSizePx(_interop.Handle, _canvasWidthPx, _canvasHeightPx);
    }

    private void ApplyFrameSize()
    {
        var clampedScale = _settings.Scale;
        _frameWidthPx = Math.Max(1, (int)Math.Round(_frameWidth * clampedScale));
        _frameHeightPx = Math.Max(1, (int)Math.Round(_frameHeight * clampedScale));
        PetViewbox.Width = PxToDipX(_frameWidthPx);
        PetViewbox.Height = PxToDipY(_frameHeightPx);
    }

    private void ApplyRoamingSpeed()
    {
        _roamSpeedPxPerSecond = _settings.Speed switch
        {
            RoamingSpeed.Slow => 60,
            RoamingSpeed.Fast => 200,
            _ => 120
        };
    }

    private void ReinitializePetPosition()
    {
        if (_canvasWidthPx <= 0 || _canvasHeightPx <= 0 || _frameWidthPx <= 0 || _frameHeightPx <= 0)
        {
            return;
        }

        var start = ObsCanvasMotion.ChooseRandomTarget(_canvasWidthPx, _canvasHeightPx, _frameWidthPx, _frameHeightPx, SafeMarginPx, _random);
        _petXpx = start.x;
        _petYpx = start.y;
        _roamStopSeconds = 0;
        _isRoaming = false;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var deltaMs = Math.Max(1.0, _clock.Elapsed.TotalMilliseconds);
        _clock.Restart();

        UpdateRoaming(deltaMs);
        var plan = ResolveAnimationPlan();
        if (_rows.TryGetValue(plan.Row, out var spec))
        {
            _sequencer.ApplyPlan(plan, spec);
            _sequencer.Tick(deltaMs, spec);
        }

        RenderFrame();
    }

    private AnimationPlan ResolveAnimationPlan()
    {
        if (_isRoaming)
        {
            return _roamDirection.X >= 0
                ? AnimationPlanFactory.CreateRowPlan(1)
                : AnimationPlanFactory.CreateRowPlan(2);
        }

        return AnimationPlanFactory.CreateRowPlan(0);
    }

    private void UpdateRoaming(double deltaMs)
    {
        if (_frameWidthPx <= 0 || _frameHeightPx <= 0 || _canvasWidthPx <= 0 || _canvasHeightPx <= 0)
        {
            return;
        }

        if (!_settings.RoamingEnabled)
        {
            _isRoaming = false;
            _roamStopSeconds = 0;
            _roamDirection = default;
            return;
        }

        if (_isRoaming)
        {
            if (_roamStopSeconds > 0)
            {
                return;
            }

            var stepPx = _roamSpeedPxPerSecond * deltaMs / 1000.0;
            var step = ObsCanvasMotion.MoveTowardTarget(
                _petXpx,
                _petYpx,
                _roamTargetX,
                _roamTargetY,
                stepPx);

            var clamped = ObsCanvasMotion.ClampToCanvas(
                _canvasWidthPx,
                _canvasHeightPx,
                _frameWidthPx,
                _frameHeightPx,
                step.NextX,
                step.NextY,
                SafeMarginPx);

            MovePetPhysical(clamped.x, clamped.y);

            if (step.ReachedTarget)
            {
                _isRoaming = false;
                _roamStopSeconds = _random.NextDouble() * 2.5 + 1.0;
                _roamDirection = default;
            }

            return;
        }

        if (_roamStopSeconds > 0)
        {
            _roamStopSeconds -= deltaMs / 1000.0;
            if (_roamStopSeconds > 0)
            {
                return;
            }
        }

        var next = ObsCanvasMotion.ChooseRandomTarget(
            _canvasWidthPx,
            _canvasHeightPx,
            _frameWidthPx,
            _frameHeightPx,
            SafeMarginPx,
            _random);

        _roamTargetX = next.x;
        _roamTargetY = next.y;
        _roamDirection = new Vector(_roamTargetX - _petXpx, _roamTargetY - _petYpx);
        _isRoaming = true;
    }

    private void MovePetPhysical(double x, double y)
    {
        var clamped = ObsCanvasMotion.ClampToCanvas(_canvasWidthPx, _canvasHeightPx, _frameWidthPx, _frameHeightPx, x, y, SafeMarginPx);
        _petXpx = clamped.x;
        _petYpx = clamped.y;
        UpdatePhysicalPosition();
    }

    private void UpdatePhysicalPosition()
    {
        if (_canvasWidthPx <= 0 || _canvasHeightPx <= 0)
        {
            return;
        }

        Canvas.SetLeft(PetViewbox, PxToDipX(_petXpx));
        Canvas.SetTop(PetViewbox, PxToDipY(_petYpx));
    }

    private void RenderFrame()
    {
        if (_sprite.Source is null)
        {
            FrameImage.Visibility = Visibility.Collapsed;
            FallbackCanvas.Visibility = Visibility.Visible;
            return;
        }

        var boundedRow = Math.Clamp(_sequencer.Row, 0, Math.Max(0, _sprite.Rows - 1));
        var boundedCol = Math.Clamp(_sequencer.Column, 0, Math.Max(0, _sprite.Columns - 1));
        if (_frameCache is null)
        {
            FrameImage.Source = null;
            return;
        }

        var frame = _frameCache.GetFrame(boundedRow, boundedCol);
        if (frame is not null && (_cachedRenderedRow != boundedRow || _cachedRenderedCol != boundedCol || !ReferenceEquals(_cachedRenderedFrame, frame)))
        {
            FrameImage.Source = frame;
        }

        FrameImage.Visibility = Visibility.Visible;
        FallbackCanvas.Visibility = Visibility.Collapsed;
        _cachedRenderedRow = boundedRow;
        _cachedRenderedCol = boundedCol;
        _cachedRenderedFrame = frame;

        if (FrameImageScaleTransform is ScaleTransform frameScaleTransform)
        {
            var scale = AnimationVisualScale.GetFrameScale(boundedRow);
            frameScaleTransform.ScaleX = scale.ScaleX;
            frameScaleTransform.ScaleY = scale.ScaleY;
        }

        if (FrameImageTranslateTransform is TranslateTransform frameTranslateTransform)
        {
            var offset = AnimationVisualOffset.GetFrameOffset(boundedRow, boundedCol);
            frameTranslateTransform.X = offset.TranslateX;
            frameTranslateTransform.Y = offset.TranslateY;
        }
    }

    private double DpiScaleX => _interop.Handle == IntPtr.Zero ? 1.0 : PetNativeWindow.GetDpiScaleX(_interop.Handle);
    private double DpiScaleY => _interop.Handle == IntPtr.Zero ? 1.0 : PetNativeWindow.GetDpiScaleY(_interop.Handle);

    private double PxToDipX(double px) => px / Math.Max(0.01, DpiScaleX);
    private double PxToDipY(double px) => px / Math.Max(0.01, DpiScaleY);

    public void CloseFromHost()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _disposed = true;
        _tickTimer.Stop();
        base.OnClosed(e);
    }
}
