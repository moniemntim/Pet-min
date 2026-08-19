using System.Windows;
using ShuBingDesktopPet.Models;
using System.ComponentModel;

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
        ClickThroughBox.IsChecked = settings.ClickThrough;
        StartWithWindowsBox.IsChecked = settings.StartWithWindows;
    }

    private void BuildControls()
    {
        RoamingScopeBox.ItemsSource = new[] { "目前螢幕", "所有螢幕" };
        SpeedBox.ItemsSource = new[] { "慢", "正常", "快" };
        RoamingScopeBox.SelectedIndex = 0;
        SpeedBox.SelectedIndex = 1;
        ScaleSlider.Value = 1.0;
        ScaleSlider.IsSnapToTickEnabled = true;

        SaveButton.Click += (_, _) =>
        {
            var next = _editing with
            {
                RoamingEnabled = RoamingEnabledBox.IsChecked == true,
                RoamingScope = RoamingScopeBox.SelectedIndex == 1 ? RoamingScope.AllScreens : RoamingScope.CurrentScreen,
                Speed = SpeedBox.SelectedIndex == 1 ? RoamingSpeed.Normal : (SpeedBox.SelectedIndex == 2 ? RoamingSpeed.Fast : RoamingSpeed.Slow),
                Scale = ScaleSlider.Value,
                ClickThrough = ClickThroughBox.IsChecked == true,
                StartWithWindows = StartWithWindowsBox.IsChecked == true
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
}
