using System.Windows;
using System.Linq;
using ShuBingDesktopPet.Models;
using System.ComponentModel;
using ShuBingDesktopPet.Services;

namespace ShuBingDesktopPet;

public partial class PetSettingsWindow : Window
{
    private readonly Action<PetSettings> _saveCallback;
    private PetSettings _editing;
    private bool _allowClose;

    public PetSettingsWindow(PetSettings settings, Action<PetSettings> saveCallback)
    {
        InitializeComponent();
        _saveCallback = saveCallback;
        _editing = settings;
        BuildControls();
        LoadFromSettings(settings);

    }

    public void ShowFrom(PetSettings settings)
    {
        LoadFromSettings(settings);
        if (!IsVisible)
        {
            Show();
        }
        Activate();
    }

    public void LoadFromSettings(PetSettings settings)
    {
        _editing = settings;
        RoamingEnabledBox.IsChecked = settings.RoamingEnabled;
        RoamingScopeBox.SelectedIndex = settings.RoamingScope == RoamingScope.AllScreens ? 1 : 0;
        SpeedBox.SelectedIndex = settings.Speed switch
        {
            RoamingSpeed.Slow => 0,
            RoamingSpeed.Fast => 2,
            _ => 1
        };
        ScaleSlider.Value = settings.Scale;
        WalkingBodyStyleBox.SelectedIndex = settings.WalkingBodyStyle == WalkingBodyStyle.LongBody ? 1 : 0;
        FeedingDurationSlider.Value = settings.FeedingDurationSeconds;
        ContinuousWalkDurationSlider.Value = settings.ContinuousWalkDurationSeconds;
        UpdateDurationLabels();
        ClickThroughBox.IsChecked = settings.ClickThrough;
        ObsCaptureEnabledBox.IsChecked = settings.ObsCaptureEnabled;
        SetObsResolution(settings.ObsCanvasWidth, settings.ObsCanvasHeight);
        StartWithWindowsBox.IsChecked = settings.StartWithWindows;
        TwitchEnabledBox.IsChecked = settings.TwitchEnabled;
        TwitchClientIdBox.Text = settings.TwitchClientId;
        TwitchMinimumBitsBox.Text = settings.TwitchMinimumBits.ToString();
    }

    private void BuildControls()
    {
        RoamingScopeBox.ItemsSource = new[] { "目前螢幕", "所有螢幕" };
        SpeedBox.ItemsSource = new[] { "慢", "正常", "快" };
        WalkingBodyStyleBox.ItemsSource = new[] { "目前身形（原本）", "修長身形" };
        ObsCaptureResolutionBox.ItemsSource = PetSettingsService.GetAllowedResolutions()
            .Select(r => $"{r.Width}x{r.Height}")
            .ToArray();
        RoamingScopeBox.SelectedIndex = 0;
        SpeedBox.SelectedIndex = 1;
        ObsCaptureResolutionBox.SelectedIndex = 1;
        ScaleSlider.Value = 1.0;
        ScaleSlider.IsSnapToTickEnabled = true;
        FeedingDurationSlider.ValueChanged += (_, _) => UpdateDurationLabels();
        ContinuousWalkDurationSlider.ValueChanged += (_, _) => UpdateDurationLabels();

        SaveButton.Click += (_, _) =>
        {
            var next = _editing with
            {
                RoamingEnabled = RoamingEnabledBox.IsChecked == true,
                RoamingScope = RoamingScopeBox.SelectedIndex == 1 ? RoamingScope.AllScreens : RoamingScope.CurrentScreen,
                Speed = SpeedBox.SelectedIndex == 1 ? RoamingSpeed.Normal : (SpeedBox.SelectedIndex == 2 ? RoamingSpeed.Fast : RoamingSpeed.Slow),
                Scale = ScaleSlider.Value,
                WalkingBodyStyle = WalkingBodyStyleBox.SelectedIndex == 1 ? WalkingBodyStyle.LongBody : WalkingBodyStyle.Current,
                FeedingDurationSeconds = Math.Round(FeedingDurationSlider.Value, 1),
                ContinuousWalkDurationSeconds = Math.Round(ContinuousWalkDurationSlider.Value, 0),
                ClickThrough = ClickThroughBox.IsChecked == true,
                ObsCaptureEnabled = ObsCaptureEnabledBox.IsChecked == true,
                ObsCanvasWidth = ObsCanvasWidth,
                ObsCanvasHeight = ObsCanvasHeight,
                StartWithWindows = StartWithWindowsBox.IsChecked == true,
                TwitchEnabled = TwitchEnabledBox.IsChecked == true,
                TwitchClientId = TwitchClientIdBox.Text.Trim(),
                TwitchMinimumBits = int.TryParse(TwitchMinimumBitsBox.Text, out var bits) ? bits : 1
            };

            _saveCallback(next);
            _editing = next;
            Hide();
        };

        CancelButton.Click += (_, _) =>
        {
            Hide();
        };
    }

    public void CloseFromHost()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private void SetObsResolution(int width, int height)
    {
        var selected = $"{width}x{height}";
        var index = ObsCaptureResolutionBox.Items.IndexOf(selected);
        if (index >= 0)
        {
            ObsCaptureResolutionBox.SelectedIndex = index;
            return;
        }

        ObsCaptureResolutionBox.SelectedIndex = 1;
        _editing = _editing with
        {
            ObsCanvasWidth = 1920,
            ObsCanvasHeight = 1080
        };
    }

    private void UpdateDurationLabels()
    {
        if (FeedingDurationLabel is not null)
        {
            FeedingDurationLabel.Text = $"{FeedingDurationSlider.Value:0.#} 秒";
        }

        if (ContinuousWalkDurationLabel is not null)
        {
            ContinuousWalkDurationLabel.Text = $"{ContinuousWalkDurationSlider.Value:0} 秒";
        }
    }

    private int ObsCanvasWidth
    {
        get
        {
            var (width, _) = ResolveObsCanvasResolution();
            return width;
        }
    }

    private int ObsCanvasHeight
    {
        get
        {
            var (_, height) = ResolveObsCanvasResolution();
            return height;
        }
    }

    private (int Width, int Height) ResolveObsCanvasResolution()
    {
        if (ObsCaptureResolutionBox.SelectedItem is not string selected)
        {
            return (1920, 1080);
        }

        var pieces = selected.Split('x', StringSplitOptions.TrimEntries);
        if (pieces.Length != 2 ||
            !int.TryParse(pieces[0], out var width) ||
            !int.TryParse(pieces[1], out var height))
        {
            return (1920, 1080);
        }

        return ObsCaptureGeometry.NormalizeResolution(width, height);
    }
}
