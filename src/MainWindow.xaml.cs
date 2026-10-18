using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OpenMedionControl.Hardware;

namespace OpenMedionControl;

public partial class MainWindow : Window
{
    static readonly string[] Swatches =
    {
        "#00A6FF", "#FFFFFF", "#FF0000", "#FF5A00", "#FFC800", "#00FF40",
        "#00FFD0", "#0030FF", "#8000FF", "#FF00C8",
    };

    static readonly TimeSpan ToggleSettleTime = TimeSpan.FromSeconds(3);

    readonly AppSettings _settings;
    readonly DispatcherTimer _poll = new();
    readonly DispatcherTimer _lightDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };

    LightZone _zone = LightZone.Keyboard;
    bool _syncingUi;
    bool _polling;
    bool _modeBusy;
    bool _exiting;
    DateTime _togglesHeldUntil;

    public event Action<HardwareStatus>? StatusChanged;

    public bool LightsOn => _settings.LightsOn;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        foreach (string hex in Swatches)
        {
            var b = new Button { Style = (Style)FindResource("Swatch"), Background = Brush(hex), ToolTip = hex };
            b.Click += (_, _) => SetColor(hex);
            SwatchPanel.Children.Add(b);
        }

        _syncingUi = true;
        LightsSwitch.IsChecked = _settings.LightsOn;
        AllZonesSwitch.IsChecked = _settings.ApplyAllZones;
        WinLockSwitch.IsChecked = _settings.WinKeyLocked;
        DedicatedGpuSwitch.IsChecked = _settings.DedicatedGpu;
        AutostartSwitch.IsChecked = Autostart.IsEnabled();
        _syncingUi = false;
        UpdateDriverUi();
        LoadZoneToUi();

        _lightDebounce.Tick += (_, _) => { _lightDebounce.Stop(); ApplyLighting(); };
        _poll.Tick += (_, _) => Poll();
        IsVisibleChanged += (_, _) =>
        {
            _poll.Interval = TimeSpan.FromSeconds(IsVisible ? 2 : 5);
            if (IsVisible) Poll();
        };
        _poll.Interval = TimeSpan.FromSeconds(5);
        _poll.Start();
        Poll();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        int dark = 1;
        DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle,
            DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void ExitApp()
    {
        _exiting = true;
        Close();
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    async void Poll()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var s = await Task.Run(HardwareStatus.Read);
            ApplyStatus(s);
        }
        finally { _polling = false; }
    }

    void ApplyStatus(HardwareStatus s)
    {
        _syncingUi = true;
        try
        {
            StatusText.Text = s.Error != null ? "EC not reachable" : (s.TurboAvailable ? "AC power" : "Battery");

            CpuTempText.Text = s.CpuTemp > 0 ? $"{s.CpuTemp} °C" : "–";
            GpuTempText.Text = s.GpuTemp > 0 ? $"{s.GpuTemp} °C" : "–";
            CpuFanText.Text = s.CpuFanRpm >= 0 ? $"Fan {s.CpuFanRpm} RPM" : "–";
            GpuFanText.Text = s.GpuFanRpm >= 0 ? $"Fan {s.GpuFanRpm} RPM" : "–";

            ModeTurbo.IsEnabled = s.TurboAvailable || s.Mode == PerformanceMode.Turbo;
            PowerHint.Visibility = s.TurboAvailable || s.Error != null ? Visibility.Collapsed : Visibility.Visible;

            if (!_modeBusy && s.Mode is { } mode)
                ModeButton(mode).IsChecked = true;

            if (DateTime.Now > _togglesHeldUntil)
            {
                if (s.FanBoost is { } fan) FanBoostSwitch.IsChecked = fan;
                if (s.UsbPowerWhenOff is { } usb) UsbPowerSwitch.IsChecked = usb;
            }
        }
        finally { _syncingUi = false; }

        StatusChanged?.Invoke(s);
    }

    RadioButton ModeButton(PerformanceMode mode) => mode switch
    {
        PerformanceMode.Office => ModeOffice,
        PerformanceMode.Gaming => ModeGaming,
        _ => ModeTurbo,
    };

    public void RequestMode(PerformanceMode mode) => ModeButton(mode).IsChecked = true;

    async void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingUi) return;
        var mode = Enum.Parse<PerformanceMode>((string)((FrameworkElement)sender).Tag);
        _modeBusy = true;
        try
        {
            bool ok = await Task.Run(() => Ec.SetMode(mode));
            if (!ok) StatusText.Text = "Could not set the mode";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { _modeBusy = false; }
        Poll();
    }

    public void RequestFanBoost(bool on)
    {
        FanBoostSwitch.IsChecked = on;
        FanBoost_Click(FanBoostSwitch, new RoutedEventArgs());
    }

    async void FanBoost_Click(object sender, RoutedEventArgs e)
    {
        bool on = FanBoostSwitch.IsChecked == true;
        _togglesHeldUntil = DateTime.Now + ToggleSettleTime;
        await RunSafe(() => Ec.SetFanBoost(on));
    }

    async void UsbPower_Click(object sender, RoutedEventArgs e)
    {
        bool on = UsbPowerSwitch.IsChecked == true;
        _togglesHeldUntil = DateTime.Now + ToggleSettleTime;
        await RunSafe(() => Ec.SetUsbPowerWhenOff(on));
    }

    async void WinLock_Click(object sender, RoutedEventArgs e)
    {
        bool on = WinLockSwitch.IsChecked == true;
        _settings.WinKeyLocked = on;
        _settings.Save();
        await RunSafe(() => Lighting.SetWinKeyLock(on));
    }

    void DedicatedGpu_Click(object sender, RoutedEventArgs e)
    {
        bool on = DedicatedGpuSwitch.IsChecked == true;
        var answer = MessageBox.Show(this,
            (on ? "Switch to the dedicated GPU only?" : "Switch to hybrid graphics (iGPU)?") +
            "\n\nThe change takes effect after a restart.\n\nYes = restart now\nNo = restart later",
            "Graphics mode", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel)
        {
            DedicatedGpuSwitch.IsChecked = !on;
            return;
        }
        try
        {
            Graphics.SetDedicatedOnly(on);
        }
        catch (Exception ex)
        {
            DedicatedGpuSwitch.IsChecked = !on;
            MessageBox.Show(this, ex.Message, "Graphics mode", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _settings.DedicatedGpu = on;
        _settings.Save();
        if (answer == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true });
    }

    void Autostart_Click(object sender, RoutedEventArgs e)
    {
        bool on = AutostartSwitch.IsChecked == true;
        if (!Autostart.Set(on))
        {
            AutostartSwitch.IsChecked = !on;
            StatusText.Text = "Could not change autostart";
        }
    }

    void UpdateDriverUi()
    {
        switch (DriverManager.GetState(_settings))
        {
            case DriverManager.State.Own:
                DriverStatusText.Text = $"Own driver service \"{DriverManager.ServiceName}\" is " +
                                        $"{DriverManager.ServiceState(DriverManager.ServiceName)?.ToLowerInvariant()}. " +
                                        "The Medion software is not needed.";
                DriverButton.Content = "Remove";
                break;
            default:
                DriverStatusText.Text = "Using the driver of the Medion software. Take it over so the Medion software can be uninstalled.";
                DriverButton.Content = "Take over…";
                break;
        }
    }

    void Driver_Click(object sender, RoutedEventArgs e)
    {
        string args;
        if (DriverManager.IsOwnInstalled)
        {
            var answer = MessageBox.Show(this,
                "Remove the driver?\n\nIf the Medion software is still installed, its driver is reactivated. " +
                "Otherwise OpenMedionControl closes and installs the driver again on its next start.",
                "Remove driver", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) return;
            args = "--uninstall-driver";
        }
        else
        {
            string? source = DriverManager.FindSource(_settings);
            if (source == null)
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select a folder containing " + string.Join(", ", DriverManager.RequiredFiles),
                };
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                source = dlg.SelectedPath;
                if (!DriverManager.HasAllFiles(source))
                {
                    MessageBox.Show(this, "Files are missing in this folder.", "Driver", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            var answer = MessageBox.Show(this,
                $"The driver and hardware DLLs will be copied to\n{DriverManager.InstallDir}\nand registered as the service " +
                $"\"{DriverManager.ServiceName}\".\n\n" +
                "MedionOSD and the Medion Control Center will be closed and the Medion service \"ioportdrv\" removed. " +
                "OpenMedionControl restarts afterwards.\n\nContinue?",
                "Take over driver", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;
            args = $"--install-driver \"{source.TrimEnd('\\')}\"";
        }

        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = true });
        ExitApp();
    }

    async Task RunSafe(Action action)
    {
        try { await Task.Run(action); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    ZoneSettings CurrentZone => _settings.Zones[_zone];

    public void RequestLights(bool on)
    {
        LightsSwitch.IsChecked = on;
        Lights_Click(LightsSwitch, new RoutedEventArgs());
    }

    async void Lights_Click(object sender, RoutedEventArgs e)
    {
        bool on = LightsSwitch.IsChecked == true;
        _settings.LightsOn = on;
        _settings.Save();
        LightingPanel.IsEnabled = on;
        await RunSafe(() =>
        {
            Lighting.SetAllOn(on);
            if (on)
            {
                foreach (var (zone, z) in _settings.Zones)
                    z.ApplyTo(zone);
            }
        });
    }

    void AllZones_Click(object sender, RoutedEventArgs e)
    {
        _settings.ApplyAllZones = AllZonesSwitch.IsChecked == true;
        _settings.Save();
        if (_settings.ApplyAllZones) QueueLightApply();
    }

    void Zone_Checked(object sender, RoutedEventArgs e)
    {
        _zone = Enum.Parse<LightZone>((string)((FrameworkElement)sender).Tag);
        if (IsInitialized) LoadZoneToUi();
    }

    void LoadZoneToUi()
    {
        var z = CurrentZone;
        _syncingUi = true;
        try
        {
            foreach (var rb in EffectGrid.Children.OfType<RadioButton>())
                rb.IsChecked = (string)rb.Tag == z.Effect.ToString();
            BrightnessSlider.Value = z.Brightness;
            SpeedSlider.Value = z.Speed;
            ReverseSwitch.IsChecked = z.Reverse;
            HexBox.Text = z.Color;
            ColorPreview.Background = Brush(z.Color);
            LightingPanel.IsEnabled = _settings.LightsOn;
            UpdateEffectDependentControls();
        }
        finally { _syncingUi = false; }
    }

    void UpdateEffectDependentControls()
    {
        bool speed = Lighting.UsesSpeed(CurrentZone.Effect);
        SpeedSlider.IsEnabled = speed;
        SpeedLabel.Opacity = speed ? 1 : 0.35;
        ReverseSwitch.Visibility = Lighting.UsesDirection(CurrentZone.Effect) ? Visibility.Visible : Visibility.Collapsed;
    }

    void Effect_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingUi) return;
        CurrentZone.Effect = Enum.Parse<LightEffect>((string)((FrameworkElement)sender).Tag);
        UpdateEffectDependentControls();
        QueueLightApply();
    }

    void Light_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingUi || !IsInitialized) return;
        CurrentZone.Brightness = (byte)BrightnessSlider.Value;
        CurrentZone.Speed = (byte)SpeedSlider.Value;
        QueueLightApply();
    }

    void Reverse_Click(object sender, RoutedEventArgs e)
    {
        CurrentZone.Reverse = ReverseSwitch.IsChecked == true;
        QueueLightApply();
    }

    void SetColor(string hex)
    {
        hex = hex.ToUpperInvariant();
        CurrentZone.Color = hex;
        HexBox.Text = hex;
        ColorPreview.Background = Brush(hex);
        QueueLightApply();
    }

    void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitHex();
    }

    void HexBox_LostFocus(object sender, RoutedEventArgs e) => CommitHex();

    void CommitHex()
    {
        string text = HexBox.Text.Trim().TrimStart('#');
        if (text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, null, out _))
            SetColor("#" + text);
        else
            HexBox.Text = CurrentZone.Color;
    }

    void ColorDialog_Click(object sender, RoutedEventArgs e)
    {
        var (r, g, b) = CurrentZone.Rgb;
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(r, g, b),
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SetColor($"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
    }

    void QueueLightApply()
    {
        _lightDebounce.Stop();
        _lightDebounce.Start();
    }

    async void ApplyLighting()
    {
        if (_settings.ApplyAllZones)
        {
            foreach (LightZone zone in Enum.GetValues<LightZone>())
                if (zone != _zone) _settings.Zones[zone] = CurrentZone.Clone();
        }
        _settings.Save();

        var targets = _settings.ApplyAllZones
            ? _settings.Zones.Select(kv => (kv.Key, kv.Value.Clone())).ToArray()
            : new[] { (_zone, CurrentZone.Clone()) };
        await RunSafe(() =>
        {
            foreach (var (zone, z) in targets)
                z.ApplyTo(zone);
        });
    }

    static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
