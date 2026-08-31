using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ShuBingDesktopPet.Models;
using ShuBingDesktopPet.Services;
using PetNativeWindow = ShuBingDesktopPet.Services.NativeWindow;
using WpfInt32Rect = System.Windows.Int32Rect;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace ShuBingDesktopPet;

public partial class MainWindow : Window
{
    private readonly PetSettingsService _settingsService = new();
    private readonly StartupManager _startupManager = new();
    private readonly NotifyIcon _trayIcon = new();
    private readonly PetSettingsWindow _settingsWindow;
    private readonly DispatcherTimer _tickTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _singleClickTimer;
    private readonly Stopwatch _clock = new();
    private readonly Random _random = new();
    private readonly SpriteSheet _sprite;
    private readonly WindowInteropHelper _interop;
    private readonly ClickArbiter _clickArbiter;
    private readonly Dictionary<int, AnimationRowSpec> _rows = new();
    private readonly SpriteFrameCache? _frameCache;
    private readonly FeedingFrameCache _feedingFrameCache;
    private readonly WalkingBodyFrameCache _walkingBodyFrameCache;
    private readonly AnimationSequencer _sequencer = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private ToolStripMenuItem _menuVisible = null!;
    private ToolStripMenuItem _menuRoaming = null!;
    private ToolStripMenuItem _menuObsCapture = null!;
    private ToolStripMenuItem _menuClickThrough = null!;
    private ToolStripMenuItem _menuStartWithWindows = null!;
    private ToolStripMenuItem _menuFeed = null!;
    private readonly FeedingController _feedingController = new();
    private readonly TwitchTokenStore _twitchTokenStore = new();
    private readonly TwitchEventSubClient _twitchClient;
    private readonly TwitchFeedQueue _twitchFeedQueue = new();
    private readonly object _twitchOperationGate = new();
    private CancellationTokenSource? _twitchOperationCts;
    private long _twitchOperationVersion;
    private ToolStripMenuItem _menuTwitch = null!;
    private ToolStripMenuItem _menuTwitchConnect = null!;
    private ObsCaptureWindow? _obsCaptureWindow;

    private PetSettings _settings;
    private double _frameWidth = 192;
    private double _frameHeight = 208;
    private int _frameWidthPx;
    private int _frameHeightPx;
    private double _roamSpeedPxPerSecond;
    private bool _isRoaming;
    private double _roamStopSeconds;
    private System.Windows.Point _roamTargetPx;
    private Vector _roamDirection;

    private bool _dragCandidate;
    private bool _dragging;
    private System.Windows.Point _dragStartDip;
    private int _dragOffsetPxX;
    private int _dragOffsetPxY;

    private bool _headZoneReady;
    private bool _headSideRight;
    private int _headSweepCount;
    private int _cachedRenderedRow = -1;
    private int _cachedRenderedCol = -1;
    private BitmapSource? _cachedRenderedFrame;

    private PetAction? _reaction;
    private bool _feedCelebrationActive;
    private DateTime _nextNaturalActionUtc = DateTime.UtcNow.AddSeconds(4);
    private double _cursorPollMs;

    private int _windowLeftPx;
    private int _windowTopPx;
    private System.Drawing.Icon? _trayIconImage;
    private bool _isSourceInitialized;
    private volatile bool _disposed;
    private Screen? _roamMonitor;
    private MonitorArea? _dragStartMonitor;
    private double _roamNoProgressMs;
    private readonly RoamingWalkTimer _roamWalkTimer = new();
    private const double HeadActionDeadZoneDip = 24.0;
    private const double GroomTriggerDistanceDip = 140.0;
    private const double GazeDistanceDip = 420.0;
    private const double SafeMarginPx = 12.0;
    private const double RoamingNoProgressMs = 250.0;
    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsService.LoadOrDefault();
        _twitchClient = new TwitchEventSubClient(_twitchTokenStore);
        _twitchClient.CheerReceived += cheer =>
        {
            PostToUiIfAvailable(() =>
            {
                if (!_disposed && _settings.TwitchEnabled && cheer.Bits >= _settings.TwitchMinimumBits)
                {
                    _twitchFeedQueue.EnqueuePellet();
                }
            });
        };
        _twitchClient.ChannelPointsRewardRedeemed += redemption =>
        {
            PostToUiIfAvailable(() =>
            {
                if (!_disposed &&
                    _settings.TwitchEnabled &&
                    _settings.TwitchChannelPointsEnabled &&
                    string.Equals(
                        redemption.RewardTitle,
                        _settings.TwitchChannelPointsRewardTitle,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _twitchFeedQueue.EnqueuePellet();
                }
            });
        };
        _twitchClient.DeviceCodeReady += (uri, code) =>
        {
            PostToUiIfAvailable(() =>
            {
                if (_disposed)
                {
                    return;
                }

                _trayIcon.ShowBalloonTip(8000, "Twitch 授權", $"請輸入驗證碼：{code}", ToolTipIcon.Info);
                try
                {
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                }
                catch
                {
                    UpdateTwitchMenu($"請開啟授權頁並輸入 {code}");
                }
            });
        };
        _twitchClient.StatusChanged += status =>
        {
            PostToUiIfAvailable(() =>
            {
                if (!_disposed)
                {
                    UpdateTwitchMenu(status.Message);
                }
            });
        };
        _sprite = SpriteSheet.Load(AppDomain.CurrentDomain.BaseDirectory);
        _interop = new WindowInteropHelper(this);
        if (_sprite.Source is BitmapSource spriteSource)
        {
            _frameCache = new SpriteFrameCache(spriteSource, _sprite.Rows, _sprite.Columns, _sprite.FrameWidth, _sprite.FrameHeight);
        }
        _feedingFrameCache = new FeedingFrameCache();
        _walkingBodyFrameCache = new WalkingBodyFrameCache();

        _settingsWindow = new PetSettingsWindow(_settings, ApplySettingsRequested);

        if (_sprite.IsValid)
        {
            _frameWidth = _sprite.FrameWidth;
            _frameHeight = _sprite.FrameHeight;
        }

        _clickArbiter = new ClickArbiter(TimeSpan.FromMilliseconds(Math.Max(1, SystemInformation.DoubleClickTime)));
        _singleClickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, SystemInformation.DoubleClickTime))
        };
        _singleClickTimer.Tick += OnSingleClickTimerTick;

        InitializeAnimationRows();
        BuildTray();
        ApplyTwitchSettings();
        ApplyWalkingBodyStyleLayout();
        ApplyWindowScale(_settings.Scale);
        ApplyRoamingSpeed();
        UpdateWindowMenus();
        if (!_settings.RoamingEnabled)
        {
            _isRoaming = false;
            _roamWalkTimer.Reset();
        }

        SourceInitialized += OnSourceInitialized;
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseRightButtonUp += OnMouseRightButtonUp;
        DpiChanged += OnDpiChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _tickTimer.Tick += OnTick;
        _clock.Start();
        _tickTimer.Start();
    }

    private void InitializeAnimationRows()
    {
        for (var i = 0; i < 11; i++)
        {
            _rows[i] = AnimationPlanFactory.RowSpec(i);
        }

        _roamMonitor = GetPrimaryOrFirstScreen();
    }

    private void BuildTray()
    {
        _menuVisible = new ToolStripMenuItem("隱藏", null, (_, _) =>
        {
            if (IsVisible)
            {
                Hide();
                _menuVisible.Text = "顯示";
            }
            else
            {
                Show();
                _menuVisible.Text = "隱藏";
            }
        });

        _menuRoaming = new ToolStripMenuItem("漫遊：關閉", null, (_, _) =>
        {
            _settings = _settings with { RoamingEnabled = !_settings.RoamingEnabled };
            ApplySettingsRequested(_settings);
        });
        _menuObsCapture = new ToolStripMenuItem("OBS 擷取：關閉", null, (_, _) =>
        {
            _settings = _settings with { ObsCaptureEnabled = !_settings.ObsCaptureEnabled };
            ApplySettingsRequested(_settings);
        });
        _menuClickThrough = new ToolStripMenuItem("滑鼠穿透：關閉", null, (_, _) =>
        {
            _settings = _settings with { ClickThrough = !_settings.ClickThrough };
            ApplySettingsRequested(_settings);
        });
        _menuStartWithWindows = new ToolStripMenuItem("開機啟動：關閉", null, (_, _) =>
        {
            _settings = _settings with { StartWithWindows = !_settings.StartWithWindows };
            ApplySettingsRequested(_settings);
        });

        var settingsItem = new ToolStripMenuItem("設定", null, (_, _) => _settingsWindow.ShowFrom(_settings));
        var feedPellet = new ToolStripMenuItem("種子棒（薯餅實拍）", null, (_, _) => TryStartFeeding(PetFoodType.Pellet));
        _menuFeed = new ToolStripMenuItem("餵食薯餅");
        _menuFeed.DropDownItems.Add(feedPellet);
        _menuTwitch = new ToolStripMenuItem("Twitch：關閉");
        _menuTwitchConnect = new ToolStripMenuItem("連線／重新授權 Twitch", null, (_, _) => ConnectTwitch());
        _menuTwitch.DropDownItems.Add(_menuTwitchConnect);
        _menuTwitch.DropDownItems.Add(new ToolStripMenuItem("中斷 Twitch", null, (_, _) => DisconnectTwitch()));
        var moveHome = new ToolStripMenuItem("回到主螢幕", null, (_, _) => MoveToPrimaryMonitor());

        var exit = new ToolStripMenuItem("退出", null, (_, _) => global::System.Windows.Application.Current?.Shutdown());

        _trayMenu.Items.Add(_menuVisible);
        _trayMenu.Items.Add(_menuRoaming);
        _trayMenu.Items.Add(_menuObsCapture);
        _trayMenu.Items.Add(_menuClickThrough);
        _trayMenu.Items.Add(_menuFeed);
        _trayMenu.Items.Add(_menuTwitch);
        _trayMenu.Items.Add(settingsItem);
        _trayMenu.Items.Add(_menuStartWithWindows);
        _trayMenu.Items.Add(moveHome);
        _trayMenu.Items.Add(exit);

        _trayIcon.Text = "薯餅桌寵";
        _trayIcon.ContextMenuStrip = _trayMenu;
        _trayIcon.Visible = true;
        if (_trayIconImage is not null)
        {
            _trayIconImage.Dispose();
            _trayIconImage = null;
        }

        _trayIconImage = TrayIconLoader.ResolveTrayIcon();
        _trayIcon.Icon = _trayIconImage ?? System.Drawing.SystemIcons.Application;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _isSourceInitialized = true;
        PetNativeWindow.EnsureLayered(_interop.Handle);
        ApplyWindowScale(_settings.Scale, applySize: true);
        ApplyClickThroughSetting(_settings.ClickThrough);
        ApplyObsCaptureWindow();
        RestoreStartupPosition();
        _startupManager.SetStartWithWindows(_settings.StartWithWindows, Environment.ProcessPath ?? string.Empty);
        UpdateWindowMenus();
        _settingsService.Save(_settingsService.Normalize(_settings));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (!_isSourceInitialized)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnDisplaySettingsChanged(sender, e));
            return;
        }

        ApplyWindowScale(_settings.Scale, applySize: true);
        EnsureInVisibleArea();
        _obsCaptureWindow?.RepositionOffscreen();
    }

    private void OnDpiChanged(object? sender, System.Windows.DpiChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnDpiChanged(sender, e));
            return;
        }

        ApplyWindowScale(_settings.Scale, applySize: true);
        EnsureInVisibleArea();
        SaveCurrentPosition();
    }

    private void ApplyWindowScale(double scale, bool applySize = true)
    {
        var clamped = PetScaleGeometry.ClampScale(scale);
        _settings = _settings with { Scale = clamped };

        var logicalSize = PetScaleGeometry.GetLogicalWindowSize(_frameWidth, _frameHeight, clamped);
        var physicalSize = PetScaleGeometry.GetPhysicalWindowSize(_frameWidth, _frameHeight, clamped, DpiScaleX, DpiScaleY);
        Width = logicalSize.WidthDip;
        Height = logicalSize.HeightDip;
        _frameWidthPx = physicalSize.WidthPx;
        _frameHeightPx = physicalSize.HeightPx;
        if (applySize && _interop.Handle != IntPtr.Zero && _frameWidthPx > 0 && _frameHeightPx > 0)
        {
            PetNativeWindow.SetWindowSizePx(_interop.Handle, _frameWidthPx, _frameHeightPx);
        }
    }

    private void RecalculatePhysicalSize()
    {
        var physicalSize = PetScaleGeometry.GetPhysicalWindowSize(_frameWidth, _frameHeight, _settings.Scale, DpiScaleX, DpiScaleY);
        _frameWidthPx = physicalSize.WidthPx;
        _frameHeightPx = physicalSize.HeightPx;
    }

    private void ApplySettingsRequested(PetSettings settings)
    {
        var previousWidthPx = _frameWidthPx;
        var previousHeightPx = _frameHeightPx;
        _settings = _settingsService.Normalize(settings);
        ApplyTwitchSettings();
        ApplyWalkingBodyStyleLayout();
        ApplyWindowScale(_settings.Scale);
        if (_isSourceInitialized && previousWidthPx > 0 && previousHeightPx > 0 &&
            (previousWidthPx != _frameWidthPx || previousHeightPx != _frameHeightPx))
        {
            MoveWindowPhysical(
                _windowLeftPx - (_frameWidthPx - previousWidthPx) / 2,
                _windowTopPx - (_frameHeightPx - previousHeightPx) / 2,
                false);
        }
        ApplyRoamingSpeed();
        ApplyClickThroughSetting(_settings.ClickThrough);
        ApplyObsCaptureWindow();
        UpdateWindowMenus();
        _startupManager.SetStartWithWindows(_settings.StartWithWindows, Environment.ProcessPath ?? string.Empty);
        _settingsService.Save(_settings);
        EnsureInVisibleArea();
        SaveCurrentPosition();

        if (!_settings.RoamingEnabled)
        {
            _isRoaming = false;
            _roamWalkTimer.Reset();
        }
    }

    private void ApplyWalkingBodyStyleLayout()
    {
        var useWideCanvas = _settings.WalkingBodyStyle == WalkingBodyStyle.LongBody &&
            _walkingBodyFrameCache.IsAvailable;
        _frameWidth = useWideCanvas ? WalkingBodyFrameCache.ExpectedFrameWidth : _sprite.FrameWidth;
        PetRenderRoot.Width = _frameWidth;
        FeedOverlay.Width = _frameWidth;
        FeedStatusText.Width = _frameWidth;
        FrameImage.Width = _sprite.FrameWidth;
        FrameImage.Height = _sprite.FrameHeight;
    }

    private void ApplyClickThroughSetting(bool clickThrough)
    {
        if (_interop.Handle == IntPtr.Zero)
        {
            return;
        }

        PetNativeWindow.SetClickThrough(_interop.Handle, clickThrough);
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

    private void UpdateWindowMenus()
    {
        _menuRoaming.Text = _settings.RoamingEnabled ? "漫遊：開啟" : "漫遊：關閉";
        _menuObsCapture.Text = _settings.ObsCaptureEnabled ? "OBS 擷取：開啟" : "OBS 擷取：關閉";
        _menuClickThrough.Text = _settings.ClickThrough ? "滑鼠穿透：開啟" : "滑鼠穿透：關閉";
        _menuStartWithWindows.Text = _settings.StartWithWindows ? "開機啟動：開啟" : "開機啟動：關閉";
        _menuTwitch.Text = _settings.TwitchEnabled ? $"Twitch：{_twitchClient.StatusMessage}" : "Twitch：關閉";
    }

    private void UpdateTwitchMenu(string message)
    {
        if (_menuTwitch is not null) _menuTwitch.Text = _settings.TwitchEnabled ? $"Twitch：{message}" : "Twitch：關閉";
    }

    private void PostToUiIfAvailable(Action action)
    {
        if (_disposed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (!_disposed && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                {
                    action();
                }
            });
        }
        catch (InvalidOperationException)
        {
            // The dispatcher can begin shutting down between the checks and BeginInvoke.
        }
    }

    private (CancellationTokenSource Source, long Version) BeginTwitchOperation()
    {
        var source = new CancellationTokenSource();
        lock (_twitchOperationGate)
        {
            var version = Interlocked.Increment(ref _twitchOperationVersion);
            var previous = _twitchOperationCts;
            _twitchOperationCts = source;
            TryCancel(previous);
            return (source, version);
        }
    }

    private void CancelTwitchOperation()
    {
        lock (_twitchOperationGate)
        {
            Interlocked.Increment(ref _twitchOperationVersion);
            var previous = _twitchOperationCts;
            _twitchOperationCts = null;
            TryCancel(previous);
        }
    }

    private void CompleteTwitchOperation(CancellationTokenSource source)
    {
        lock (_twitchOperationGate)
        {
            if (ReferenceEquals(_twitchOperationCts, source))
            {
                _twitchOperationCts = null;
            }

            // Cancel() may inline the async continuation that reaches this method.
            // Leave canceled sources to the GC so they are never disposed mid-callback.
            if (!source.IsCancellationRequested)
            {
                source.Dispose();
            }
        }
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ApplyTwitchSettings()
    {
        if (!_settings.TwitchEnabled)
        {
            CancelTwitchOperation();
            _twitchClient.Stop();
            _twitchFeedQueue.Clear();
            return;
        }

        var clientId = _settings.TwitchClientId;
        var enableChannelPoints = _settings.TwitchChannelPointsEnabled;
        var (operationCts, operationVersion) = BeginTwitchOperation();
        _ = Task.Run(async () =>
        {
            try
            {
                await _twitchClient.StartAsync(clientId, enableChannelPoints, operationCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                PostToUiIfAvailable(() =>
                {
                    if (Volatile.Read(ref _twitchOperationVersion) == operationVersion &&
                        _settings.TwitchEnabled &&
                        string.Equals(_settings.TwitchClientId, clientId, StringComparison.Ordinal) &&
                        _settings.TwitchChannelPointsEnabled == enableChannelPoints)
                    {
                        UpdateTwitchMenu("錯誤：" + ex.Message);
                    }
                });
            }
            finally
            {
                CompleteTwitchOperation(operationCts);
            }
        });
    }

    private void ConnectTwitch()
    {
        if (!_settings.TwitchEnabled || string.IsNullOrWhiteSpace(_settings.TwitchClientId))
        {
            _trayIcon.ShowBalloonTip(5000, "Twitch", "請先在設定填寫 Client ID 並啟用 Twitch。", ToolTipIcon.Warning);
            return;
        }

        var clientId = _settings.TwitchClientId;
        var enableChannelPoints = _settings.TwitchChannelPointsEnabled;
        var (operationCts, operationVersion) = BeginTwitchOperation();
        _ = Task.Run(async () =>
        {
            try
            {
                await _twitchClient.AuthorizeAsync(clientId, enableChannelPoints, operationCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                PostToUiIfAvailable(() =>
                {
                    if (Volatile.Read(ref _twitchOperationVersion) == operationVersion &&
                        _settings.TwitchEnabled &&
                        string.Equals(_settings.TwitchClientId, clientId, StringComparison.Ordinal) &&
                        _settings.TwitchChannelPointsEnabled == enableChannelPoints)
                    {
                        UpdateTwitchMenu("錯誤：" + ex.Message);
                    }
                });
            }
            finally
            {
                CompleteTwitchOperation(operationCts);
            }
        });
    }

    private void DisconnectTwitch()
    {
        CancelTwitchOperation();
        _twitchClient.Stop();
        _twitchTokenStore.Clear();
        _twitchFeedQueue.Clear();
        _settings = _settings with { TwitchEnabled = false };
        _settingsService.Save(_settings);
        UpdateWindowMenus();
    }

    private void ApplyObsCaptureWindow()
    {
        if (_settings.ObsCaptureEnabled)
        {
            EnsureObsCaptureWindow();
            return;
        }

        CloseObsCaptureWindow();
    }

    private void EnsureObsCaptureWindow()
    {
        if (_obsCaptureWindow is null)
        {
            var window = new ObsCaptureWindow(
                _sprite,
                _frameCache,
                _feedingFrameCache,
                _walkingBodyFrameCache,
                _settings);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_obsCaptureWindow, window))
                {
                    _obsCaptureWindow = null;
                }
            };
            _obsCaptureWindow = window;
            SyncObsCaptureState();
            window.Show();
            return;
        }

        _obsCaptureWindow.ApplySettings(_settings);
        SyncObsCaptureState();
    }

    private void CloseObsCaptureWindow()
    {
        if (_obsCaptureWindow is null)
        {
            return;
        }

        _obsCaptureWindow.CloseFromHost();
        _obsCaptureWindow = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var deltaMs = Math.Max(1.0, _clock.Elapsed.TotalMilliseconds);
        _clock.Restart();
        var now = DateTime.UtcNow;
        if (_settings.TwitchEnabled)
        {
            _twitchFeedQueue.TryStartFeeding(TryStartFeeding);
        }

        UpdateCursorBehavior(deltaMs);
        UpdateRoaming(deltaMs);
        var plan = ResolveAnimationPlan();
        if (_rows.TryGetValue(plan.Row, out var spec))
        {
            _sequencer.ApplyPlan(plan, spec);
            _sequencer.Tick(deltaMs, spec);

            if (_reaction == PetAction.Feed && _feedingController.TryComplete(now, out var dueFood))
            {
                CompleteFeeding(now, dueFood);
            }
            else if (_sequencer.CompletedOneShot && _reaction is not null && plan.IsOneShot)
            {
                if (_reaction == PetAction.HappyJump && _feedCelebrationActive)
                {
                    _feedCelebrationActive = false;
                    SetReaction(null);
                }
                else
                {
                    SetReaction(null);
                }
            }
        }

        SyncObsCaptureState();
        RenderFrame();
        RenderFeedOverlay(now);
    }

    private void SyncObsCaptureState()
    {
        _obsCaptureWindow?.ApplyPetState(
            _reaction,
            _feedingController.IsActive,
            _feedingController.ActiveFood);
    }

    private void CompleteFeeding(DateTime now, PetFoodType completedFood)
    {
        _settings = _settingsService.Normalize(_settings with
        {
            TotalFeedCount = _settings.TotalFeedCount < int.MaxValue ? _settings.TotalFeedCount + 1 : int.MaxValue,
            LastFedUtc = now,
            LastFoodType = completedFood
        });
        _settingsService.Save(_settings);
        _feedCelebrationActive = true;
        SetReaction(PetAction.HappyJump);
    }

    private AnimationPlan ResolveAnimationPlan()
    {
        if (_dragging)
        {
            return AnimationPlanFactory.CreateRowPlan(5);
        }

        if (_reaction.HasValue)
        {
            if (_reaction == PetAction.Feed)
            {
                return new AnimationPlan(7, Loop: true, IsOneShot: false);
            }

            return AnimationPlanFactory.CreateRowPlanForAction(_reaction.Value);
        }

        if (_settings.RoamingEnabled && _isRoaming)
        {
            return _roamDirection.X >= 0
                ? AnimationPlanFactory.CreateRowPlan(1)
                : AnimationPlanFactory.CreateRowPlan(2);
        }

        var cursorPx = PetNativeWindow.GetCursorPositionPx();
        var centerPx = new System.Windows.Point(_windowLeftPx + _frameWidthPx / 2.0, _windowTopPx + _frameHeightPx / 2.0);
        var center = new System.Windows.Point(PxToDipX(centerPx.X), PxToDipY(centerPx.Y));
        var now = DateTime.UtcNow;
        var centerDeadZonePx = Math.Max(1.0, (DpiScaleX + DpiScaleY) * 0.5 * HeadActionDeadZoneDip);
        var groomDistancePx = Math.Max(1.0, (DpiScaleX + DpiScaleY) * 0.5 * GroomTriggerDistanceDip);
        var gazeDistancePx = Math.Max(1.0, (DpiScaleX + DpiScaleY) * 0.5 * GazeDistanceDip);
        var cursorDistancePx = Math.Sqrt(
            Math.Pow(cursorPx.x - centerPx.X, 2) + Math.Pow(cursorPx.y - centerPx.Y, 2));

        if (cursorDistancePx > centerDeadZonePx && cursorDistancePx < groomDistancePx && now >= _nextNaturalActionUtc)
        {
            _nextNaturalActionUtc = now.AddSeconds(3 + _random.NextDouble() * 3);
            SetReaction(PetAction.Groom);
            return AnimationPlanFactory.CreateRowPlan(7);
        }

        if (cursorDistancePx <= centerDeadZonePx)
        {
            return AnimationPlanFactory.CreateRowPlan(0);
        }

        if (cursorDistancePx < gazeDistancePx)
        {
            var cursorDip = GetCursorPositionDip();
            var dir16 = PetGeometry.Direction16FromMouse(center, cursorDip);
            var gazeRow = dir16 < 8 ? 9 : 10;
            return AnimationPlanFactory.CreateRowPlan(gazeRow, dir16 % 8);
        }

        return AnimationPlanFactory.CreateRowPlan(0);
    }

    private void UpdateCursorBehavior(double dtMs)
    {
        if (IsFeedingSequenceActive)
        {
            _headZoneReady = false;
            _headSweepCount = 0;
            return;
        }

        _cursorPollMs += dtMs;
        if (_cursorPollMs < 80.0)
        {
            return;
        }

        _cursorPollMs = 0;
        var cursorPx = PetNativeWindow.GetCursorPositionPx();
        var localCursor = new System.Windows.Point(
            (cursorPx.x - _windowLeftPx) / DpiScaleX,
            (cursorPx.y - _windowTopPx) / DpiScaleY);
        var usesWideWalkingFrame = _settings.WalkingBodyStyle == WalkingBodyStyle.LongBody &&
            _walkingBodyFrameCache.IsAvailable && _settings.RoamingEnabled && _isRoaming;
        var contentWidth = usesWideWalkingFrame ? WalkingBodyFrameCache.ExpectedFrameWidth : _sprite.FrameWidth;
        var contentLeft = Math.Max(0, (_frameWidth - contentWidth) * _settings.Scale / 2.0);
        var localFrame = new Rect(contentLeft, 0, contentWidth * _settings.Scale, _frameHeight * _settings.Scale);

        if (PetGeometry.IsInHeadZone(localFrame, localCursor))
        {
            var sideRight = localCursor.X >= localFrame.X + localFrame.Width / 2;
            if (_headZoneReady && _headSideRight != sideRight)
            {
                _headSweepCount++;
                if (_headSweepCount >= 3)
                {
                    SetReaction(PetAction.Petted);
                    _headSweepCount = 0;
                    _headZoneReady = false;
                }
            }

            _headZoneReady = true;
            _headSideRight = sideRight;
        }
        else
        {
            _headZoneReady = false;
            _headSweepCount = 0;
        }
    }

    private void UpdateRoaming(double dtMs)
    {
        if (!_settings.RoamingEnabled || _dragCandidate || _dragging || _reaction is not null)
        {
            return;
        }

        if (_isRoaming)
        {
            if (_roamStopSeconds > 0)
            {
                _roamNoProgressMs = 0;
                return;
            }

            if (_roamWalkTimer.Advance(dtMs / 1000.0))
            {
                _isRoaming = false;
                _roamStopSeconds = _random.NextDouble() * 2.5 + 1;
                _roamNoProgressMs = 0;
                return;
            }

            var stepPx = _roamSpeedPxPerSecond * dtMs / 1000.0;
            var step = RoamingMotion.CalculateNextStep(_windowLeftPx, _windowTopPx, _roamTargetPx.X, _roamTargetPx.Y, stepPx);
            var previousLeftPx = _windowLeftPx;
            var previousTopPx = _windowTopPx;

            var clampToCurrentScreenOnly = _settings.RoamingScope == RoamingScope.CurrentScreen;

            if (step.ReachedTarget)
            {
                MoveWindowPhysical((int)Math.Round(_roamTargetPx.X), (int)Math.Round(_roamTargetPx.Y), clampToCurrentScreenOnly);
                _roamTargetPx = NextRoamTarget();
                _roamDirection = _roamTargetPx - new System.Windows.Point(_windowLeftPx, _windowTopPx);
                _roamNoProgressMs = 0;
            }
            else
            {
                MoveWindowForRoaming(step.NextX, step.NextY);

                if (RoamingMotion.HasNoMovement(previousLeftPx, previousTopPx, _windowLeftPx, _windowTopPx))
                {
                    _roamNoProgressMs += dtMs;
                    if (_roamNoProgressMs >= RoamingNoProgressMs)
                    {
                        _roamTargetPx = NextRoamTarget();
                        _roamDirection = _roamTargetPx - new System.Windows.Point(_windowLeftPx, _windowTopPx);
                        _roamNoProgressMs = 0;
                    }
                }
                else
                {
                    _roamNoProgressMs = 0;
                }
            }

            return;
        }

        if (_roamStopSeconds > 0)
        {
            _roamStopSeconds -= dtMs / 1000.0;
            if (_roamStopSeconds > 0)
            {
                return;
            }
        }

        _roamTargetPx = NextRoamTarget();
        _roamDirection = _roamTargetPx - new System.Windows.Point(_windowLeftPx, _windowTopPx);
        _isRoaming = true;
        _roamWalkTimer.Start(_settings.ContinuousWalkDurationSeconds);
        _roamNoProgressMs = 0;
    }

    private System.Windows.Point NextRoamTarget()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0)
        {
            return new System.Windows.Point(_windowLeftPx, _windowTopPx);
        }

        var chosen = _settings.RoamingScope == RoamingScope.AllScreens
            ? screens[_random.Next(screens.Length)]
            : ScreenFromWindow();
        _roamMonitor = chosen;

        var area = new Rect(
            chosen.WorkingArea.Left,
            chosen.WorkingArea.Top,
            chosen.WorkingArea.Width,
            chosen.WorkingArea.Height);
        var width = Math.Max(1, _frameWidthPx);
        var height = Math.Max(1, _frameHeightPx);
        var bounds = RoamingBoundary.GetSafeMovementBounds(area, width, height, SafeMarginPx);
        var spanX = Math.Max(0.0, bounds.MaxX - bounds.MinX);
        var spanY = Math.Max(0.0, bounds.MaxY - bounds.MinY);

        return new System.Windows.Point(
            bounds.MinX + _random.NextDouble() * spanX,
            bounds.MinY + _random.NextDouble() * spanY);
    }

    private void MoveWindowForRoaming(double nextX, double nextY)
    {
        var currentMonitor = ScreenFromWindow();
        if (_settings.RoamingScope == RoamingScope.CurrentScreen || _roamMonitor is null)
        {
            var currentArea = new Rect(currentMonitor.WorkingArea.Left, currentMonitor.WorkingArea.Top, currentMonitor.WorkingArea.Width, currentMonitor.WorkingArea.Height);
            var clamped = RoamingBoundary.ClampToVisible(currentArea, _frameWidthPx, _frameHeightPx, nextX, nextY, SafeMarginPx);
            MoveWindowPhysical((int)Math.Round(clamped.x), (int)Math.Round(clamped.y), false);
            return;
        }

        var destinationArea = new Rect(
            _roamMonitor.WorkingArea.Left,
            _roamMonitor.WorkingArea.Top,
            _roamMonitor.WorkingArea.Width,
            _roamMonitor.WorkingArea.Height);

        var sourceArea = new Rect(
            currentMonitor.WorkingArea.Left,
            currentMonitor.WorkingArea.Top,
            currentMonitor.WorkingArea.Width,
            currentMonitor.WorkingArea.Height);

        if (AreSameMonitor(currentMonitor, _roamMonitor))
        {
            var clamped = RoamingBoundary.ClampToVisible(sourceArea, _frameWidthPx, _frameHeightPx, nextX, nextY, SafeMarginPx);
            MoveWindowPhysical((int)Math.Round(clamped.x), (int)Math.Round(clamped.y), false);
            return;
        }

        var resolved = RoamingScreenTransition.Resolve(
            sourceArea,
            destinationArea,
            _frameWidthPx,
            _frameHeightPx,
            _windowLeftPx,
            _windowTopPx,
            nextX,
            nextY);

        MoveWindowPhysical((int)Math.Round(resolved.X), (int)Math.Round(resolved.Y), false);
        if (resolved.CrossedToDestination)
        {
            _roamDirection = RoamingScreenTransition.DirectionToTarget(_windowLeftPx, _windowTopPx, _roamTargetPx.X, _roamTargetPx.Y);
        }
    }

    private void OnMouseDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (_settings.ClickThrough || IsFeedingSequenceActive)
        {
            return;
        }

        _dragCandidate = true;
        _dragStartDip = e.GetPosition(this);
        _dragStartMonitor = ToMonitorArea(ScreenFromWindow());
        var cursorPx = PetNativeWindow.GetCursorPositionPx();
        var windowRect = PetNativeWindow.GetWindowRectPx(_interop.Handle);
        _dragOffsetPxX = cursorPx.x - windowRect.x;
        _dragOffsetPxY = cursorPx.y - windowRect.y;
        _dragging = false;
        CaptureMouse();
        _settingsWindow.Hide();
    }

    private void OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_dragCandidate || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (!_dragging)
        {
            if (!PetGeometry.IsDragStart(_dragStartDip, e.GetPosition(this)))
            {
                return;
            }

            _dragging = true;
            _clickArbiter.CancelPending();
            _singleClickTimer.Stop();
            SetReaction(null);
        }

        if (_dragging)
        {
            var cursor = PetNativeWindow.GetCursorPositionPx();
            var nextLeftPx = cursor.x - _dragOffsetPxX;
            var nextTopPx = cursor.y - _dragOffsetPxY;
            var clamped = DragPlacement.ResolveDragMove(
                nextLeftPx,
                nextTopPx,
                _frameWidthPx,
                _frameHeightPx,
                SafeMarginPx,
                _settings.RoamingScope,
                _dragStartMonitor);

            MoveWindowPhysical((int)Math.Round(clamped.x), (int)Math.Round(clamped.y), false);
        }
    }

    private void OnMouseUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_dragCandidate)
        {
            return;
        }

        if (_dragging)
        {
            _clickArbiter.CancelPending();
            _singleClickTimer.Stop();
            var cursor = PetNativeWindow.GetCursorPositionPx();
            var monitorAreas = GetMonitorAreas();
            var drop = DragPlacement.ResolveDragDrop(
                _windowLeftPx,
                _windowTopPx,
                _frameWidthPx,
                _frameHeightPx,
                SafeMarginPx,
                _settings.RoamingScope,
                monitorAreas,
                cursor.x,
                cursor.y,
                _dragStartMonitor);
            MoveWindowPhysical((int)Math.Round(drop.x), (int)Math.Round(drop.y), false);
            _roamMonitor = GetMonitorByDeviceName(drop.destination.DeviceName);
            _dragStartMonitor = null;
            _isRoaming = false;
            _roamStopSeconds = 1;
            _roamNoProgressMs = 0;
            _dragging = false;
            SaveCurrentPosition();
            _dragCandidate = false;
            ReleaseMouseCapture();
            return;
        }

        _dragCandidate = false;
        ReleaseMouseCapture();
        if (IsFeedingSequenceActive)
        {
            return;
        }

        var decision = _clickArbiter.OnMouseUp(e.ClickCount, DateTime.UtcNow);

        switch (decision)
        {
            case ClickDecision.Double:
                if (_singleClickTimer.IsEnabled)
                {
                    _singleClickTimer.Stop();
                }
                SetReaction(PetAction.HappyJump);
                break;

            case ClickDecision.SingleStarted:
                _singleClickTimer.Start();
                break;
        }
    }

    private void OnMouseRightButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (_settings.ClickThrough)
        {
            return;
        }

        var cursor = PetNativeWindow.GetCursorPositionPx();
        _trayMenu.Show(cursor.x, cursor.y);
        e.Handled = true;
    }

    private bool TryStartFeeding(PetFoodType foodType)
    {
        var now = DateTime.UtcNow;
        var blocked = _dragging || _dragCandidate || _reaction is not null;
        if (_feedingController.TryStart(foodType, now, blocked, _settings.FeedingDurationSeconds))
        {
            _singleClickTimer.Stop();
            _clickArbiter.CancelPending();
            _feedCelebrationActive = false;
            SetReaction(PetAction.Feed);
            return true;
        }
        return false;
    }

    private void OnSingleClickTimerTick(object? sender, EventArgs e)
    {
        _singleClickTimer.Stop();
        if (IsFeedingSequenceActive)
        {
            _clickArbiter.CancelPending();
            return;
        }

        if (_clickArbiter.TryConsumeWave(DateTime.UtcNow))
        {
            SetReaction(PetAction.Wave);
        }
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
        if (_frameCache is not { } frameCache)
        {
            FrameImage.Source = null;
            return;
        }

        var isFeedRender = _reaction == PetAction.Feed && _feedingController.IsActive;
        var feedingFrame = isFeedRender
            ? _feedingFrameCache.GetFrame(_feedingController.ActiveFood, boundedCol)
            : null;
        var usingFeedingFrame = feedingFrame is not null;
        var isLongBodyWalk = _walkingBodyFrameCache.IsAvailable &&
            _settings.WalkingBodyStyle == WalkingBodyStyle.LongBody &&
            _settings.RoamingEnabled && _isRoaming && (boundedRow is 1 or 2);
        var longBodyFrame = isLongBodyWalk
            ? _walkingBodyFrameCache.GetFrame(boundedRow == 1, boundedCol)
            : null;
        BitmapSource? frame = feedingFrame ?? longBodyFrame ?? frameCache.GetFrame(boundedRow, boundedCol);

        if (frame is not null)
        {
            if (_cachedRenderedRow != boundedRow || _cachedRenderedCol != boundedCol || !ReferenceEquals(_cachedRenderedFrame, frame))
            {
                FrameImage.Source = frame;
            }
        }
        else
        {
            FrameImage.Source = null;
        }

        FrameImage.Visibility = Visibility.Visible;
        FallbackCanvas.Visibility = Visibility.Hidden;
        _cachedRenderedRow = boundedRow;
        _cachedRenderedCol = boundedCol;
        _cachedRenderedFrame = frame;
        FrameImage.Width = frame?.PixelWidth ?? _sprite.FrameWidth;
        FrameImage.Height = frame?.PixelHeight ?? _sprite.FrameHeight;
        if (FrameImageScaleTransform is ScaleTransform frameScaleTransform)
        {
            var scale = usingFeedingFrame || longBodyFrame is not null
                ? new AnimationFrameScale(1, 1)
                : AnimationVisualScale.GetFrameScale(boundedRow);
            frameScaleTransform.ScaleX = scale.ScaleX;
            frameScaleTransform.ScaleY = scale.ScaleY;
        }

        if (FrameImageTranslateTransform is TranslateTransform frameTranslateTransform)
        {
            if (usingFeedingFrame || longBodyFrame is not null)
            {
                frameTranslateTransform.X = 0;
                frameTranslateTransform.Y = 0;
            }
            else
            {
                var offset = AnimationVisualOffset.GetFrameOffset(boundedRow, boundedCol);
                frameTranslateTransform.X = offset.TranslateX;
                frameTranslateTransform.Y = offset.TranslateY;
            }
        }
    }

    private void RenderFeedOverlay(DateTime nowUtc)
    {
        var isMessageVisible = _feedingController.IsCompletedMessageVisible(nowUtc);

        if (!isMessageVisible)
        {
            FeedOverlay.Visibility = Visibility.Collapsed;
            FeedStatusText.Text = string.Empty;
            return;
        }

        FeedOverlay.Visibility = Visibility.Visible;
        FeedStatusText.TextAlignment = TextAlignment.Center;
        FeedStatusText.Text = "薯餅吃飽了！";
    }

    private void RestoreStartupPosition()
    {
        var saved = _settings;
        var defaultScreen = System.Windows.Forms.Screen.PrimaryScreen;
        var preferred = !string.IsNullOrWhiteSpace(saved.LastMonitorId)
            ? GetMonitorByDeviceName(saved.LastMonitorId)
            : null;

        if (preferred == null || preferred.WorkingArea.Width <= 0 || preferred.WorkingArea.Height <= 0)
        {
            preferred = defaultScreen;
        }

        if (preferred == null)
        {
            return;
        }

        _roamMonitor = preferred;
        var width = Math.Max(1, _frameWidthPx);
        var height = Math.Max(1, _frameHeightPx);

        var xPx = double.IsFinite(saved.LastX) ? (int)Math.Round(saved.LastX) : 120;
        var yPx = double.IsFinite(saved.LastY) ? (int)Math.Round(saved.LastY) : 120;
        var preferredArea = new Rect(
            preferred.WorkingArea.Left,
            preferred.WorkingArea.Top,
            preferred.WorkingArea.Width,
            preferred.WorkingArea.Height);
        var clampedStart = RoamingBoundary.ClampToVisible(preferredArea, width, height, xPx, yPx, SafeMarginPx);
        var x = (int)Math.Round(clampedStart.x);
        var y = (int)Math.Round(clampedStart.y);

        MoveWindowPhysical(x, y, true);
    }

    private void SaveCurrentPosition()
    {
        var center = new System.Windows.Point(_windowLeftPx + _frameWidthPx / 2, _windowTopPx + _frameHeightPx / 2);
        var currentMonitor = ScreenFromPoint((int)center.X, (int)center.Y);
        _settings = _settings with
        {
            LastX = _windowLeftPx,
            LastY = _windowTopPx,
            LastMonitorId = currentMonitor.DeviceName
        };
        _settingsService.Save(_settings);
    }

    private void EnsureInVisibleArea()
    {
        MoveWindowPhysical(_windowLeftPx, _windowTopPx, true);
    }

    private void MoveWindowPhysical(int leftPx, int topPx, bool clampToMonitor)
    {
        if (_frameWidthPx <= 0 || _frameHeightPx <= 0)
        {
            RecalculatePhysicalSize();
        }

        var width = _frameWidthPx;
        var height = _frameHeightPx;
        var x = leftPx;
        var y = topPx;

        if (clampToMonitor)
        {
            var clampArea = SelectMonitorForMovement();
            var wa = new Rect(
                clampArea.WorkingArea.Left,
                clampArea.WorkingArea.Top,
                clampArea.WorkingArea.Width,
                clampArea.WorkingArea.Height);
            var clamped = RoamingBoundary.ClampToVisible(wa, width, height, x, y, SafeMarginPx);
            x = (int)clamped.x;
            y = (int)clamped.y;
        }

        _windowLeftPx = x;
        _windowTopPx = y;

        if (_interop.Handle != IntPtr.Zero)
        {
            PetNativeWindow.MoveWindowPx(_interop.Handle, x, y, width, height);
        }
    }

    private Screen SelectMonitorForMovement()
    {
        if (_settings.RoamingEnabled && _isRoaming)
        {
            if (_roamMonitor is not null && !string.IsNullOrWhiteSpace(_roamMonitor.DeviceName))
            {
                var monitors = Screen.AllScreens;
                foreach (var monitor in monitors)
                {
                    if (AreSameMonitor(monitor, _roamMonitor))
                    {
                        return monitor;
                    }
                }
            }
        }

        var pointMonitor = ScreenFromPoint(_windowLeftPx, _windowTopPx);
        var screens = Screen.AllScreens;
        foreach (var screen in screens)
        {
            if (AreSameMonitor(screen, pointMonitor))
            {
                return screen;
            }
        }

        return GetPrimaryOrFirstScreen();
    }

    private static bool AreSameMonitor(Screen a, Screen? b)
    {
        if (b is null)
        {
            return false;
        }

        return string.Equals(a.DeviceName, b.DeviceName, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureInMonitor(Screen? target)
    {
        if (target is null)
        {
            return;
        }

        var width = Math.Max(1, _frameWidthPx);
        var height = Math.Max(1, _frameHeightPx);
        var targetArea = new Rect(
            target.WorkingArea.Left,
            target.WorkingArea.Top,
            target.WorkingArea.Width,
            target.WorkingArea.Height);
        var bounds = RoamingBoundary.GetSafeMovementBounds(targetArea, width, height, SafeMarginPx);
        var clamped = RoamingBoundary.ClampToVisible(targetArea, width, height, bounds.MaxX, bounds.MaxY, SafeMarginPx);
        MoveWindowPhysical((int)clamped.x, (int)clamped.y, false);
    }

    private Screen ScreenFromWindow()
    {
        return ScreenFromPoint(_windowLeftPx, _windowTopPx);
    }

    private static Screen ScreenFromPoint(int x, int y)
    {
        try
        {
            return Screen.FromPoint(new System.Drawing.Point(x, y));
        }
        catch
        {
            return GetPrimaryOrFirstScreen();
        }
    }

    private static Screen GetMonitorByDeviceName(string deviceName)
    {
        var allScreens = Screen.AllScreens;
        foreach (var screen in allScreens)
        {
            if (string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return screen;
            }
        }

        return allScreens.Length > 0 ? allScreens[0] : GetPrimaryOrFirstScreen();
    }

    private static Screen GetPrimaryOrFirstScreen()
    {
        if (Screen.PrimaryScreen is { } primary)
        {
            return primary;
        }

        var all = Screen.AllScreens;
        if (all.Length > 0)
        {
            return all[0];
        }

        throw new InvalidOperationException("No display monitor is available.");
    }

    private void MoveToPrimaryMonitor()
    {
        var primary = Screen.PrimaryScreen;
        if (primary == null)
        {
            return;
        }

        var primaryArea = new Rect(
            primary.WorkingArea.Left,
            primary.WorkingArea.Top,
            primary.WorkingArea.Width,
            primary.WorkingArea.Height);
        var bounds = RoamingBoundary.GetSafeMovementBounds(primaryArea, Math.Max(1, _frameWidthPx), Math.Max(1, _frameHeightPx), SafeMarginPx);
        MoveWindowPhysical((int)Math.Round(bounds.MaxX), (int)Math.Round(bounds.MaxY), true);
        SaveCurrentPosition();
    }

    private static IReadOnlyList<MonitorArea> GetMonitorAreas()
    {
        var screens = Screen.AllScreens;
        var monitorAreas = new List<MonitorArea>(screens.Length);
        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            monitorAreas.Add(new MonitorArea(
                new Rect(
                    screen.WorkingArea.Left,
                    screen.WorkingArea.Top,
                    screen.WorkingArea.Width,
                    screen.WorkingArea.Height),
                screen.DeviceName));
        }

        return monitorAreas;
    }

    private static MonitorArea ToMonitorArea(Screen screen)
    {
        return new MonitorArea(
            new Rect(
                screen.WorkingArea.Left,
                screen.WorkingArea.Top,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height),
            screen.DeviceName);
    }

    private double DpiScaleX
    {
        get
        {
            if (_interop.Handle == IntPtr.Zero)
            {
                return 1.0;
            }

            return PetNativeWindow.GetDpiScaleX(_interop.Handle);
        }
    }

    private double DpiScaleY
    {
        get
        {
            if (_interop.Handle == IntPtr.Zero)
            {
                return 1.0;
            }

            return PetNativeWindow.GetDpiScaleY(_interop.Handle);
        }
    }

    private int DipToPxX(double dip) => (int)Math.Round(dip * DpiScaleX, 0, MidpointRounding.AwayFromZero);
    private int DipToPxY(double dip) => (int)Math.Round(dip * DpiScaleY, 0, MidpointRounding.AwayFromZero);
    private double PxToDipX(double px) => px / DpiScaleX;
    private double PxToDipY(double px) => px / DpiScaleY;

    private System.Windows.Point GetCursorPositionDip()
    {
        var cursor = PetNativeWindow.GetCursorPositionPx();
        return new System.Windows.Point(PxToDipX(cursor.x), PxToDipY(cursor.y));
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelTwitchOperation();
        _twitchClient.Stop();
        _feedingController.Cancel();
        _feedCelebrationActive = false;
        _tickTimer.Stop();
        _singleClickTimer.Stop();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip = null;
        _trayMenu.Dispose();
        _trayIcon.Dispose();
        if (_trayIconImage is not null)
        {
            _trayIconImage.Dispose();
            _trayIconImage = null;
        }
        _settingsWindow.CloseFromHost();
        CloseObsCaptureWindow();
        _twitchClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
        SaveCurrentPosition();
        base.OnClosed(e);
    }

    private void SetReaction(PetAction? action)
    {
        if (_reaction == action)
        {
            _sequencer.Reset();
            return;
        }

        _reaction = action;
    }

    private bool IsFeedingSequenceActive => _feedingController.IsActive || _feedCelebrationActive;
}
