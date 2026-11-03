using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TensileNeW.Services;

namespace TensileNeW.Controls;

public partial class VisionCameraPanel : UserControl
{
    private readonly DispatcherTimer _parameterTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _saveTimer = new();
    private IVisionCameraService? _service;
    private BitmapSource? _latestImage;
    private readonly object _frameDispatchLock = new();
    private BitmapSource? _pendingImage;
    private bool _frameDispatchPending;
    private bool _parameterReadPending;
    private bool _imageSavePending;

    public VisionCameraPanel()
    {
        InitializeComponent();
        SavePathTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ECS");
        _parameterTimer.Tick += async (_, _) => await ReadParametersAsync(false);
        _saveTimer.Tick += async (_, _) => await SaveLatestImageAsync();
    }

    public void Configure(IVisionCameraService service)
    {
        _service = service;
        _service.FrameArrived += Service_FrameArrived;
        _service.CaptureFailed += Service_CaptureFailed;
    }

    public Task ActivateAsync() => RefreshDevicesAsync();

    public async ValueTask ReleaseAsync()
    {
        _parameterTimer.Stop();
        _saveTimer.Stop();
        if (_service is not null)
        {
            _service.FrameArrived -= Service_FrameArrived;
            _service.CaptureFailed -= Service_CaptureFailed;
            await _service.DisposeAsync();
            _service = null;
        }
    }

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private async Task RefreshDevicesAsync()
    {
        if (_service is null) return;
        try
        {
            DeviceComboBox.ItemsSource = await _service.EnumerateDevicesAsync();
            DeviceComboBox.SelectedIndex = DeviceComboBox.Items.Count > 0 ? 0 : -1;
            SetStatus(DeviceComboBox.Items.Count > 0 ? "Camera ready" : "No camera found");
        }
        catch (Exception exception) { SetStatus(exception.Message); }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null || DeviceComboBox.SelectedItem is not VisionCameraDevice device) return;
        ConnectButton.IsEnabled = false;
        try
        {
            await _service.ConnectAsync(device.Id);
            ConnectionStatusText.Text = "Connected";
            ConnectButton.Content = "Connected";
            StartCaptureButton.IsEnabled = true;
            await ReadParametersAsync(true);
            _parameterTimer.Start();
        }
        catch (Exception exception) { SetStatus(exception.Message); }
        finally { ConnectButton.IsEnabled = true; }
    }

    private async void StartCapture_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        try
        {
            await _service.StartCaptureAsync();
            StartCaptureButton.IsEnabled = false;
            StopCaptureButton.IsEnabled = true;
            ImageHintText.Visibility = Visibility.Collapsed;
            SetStatus("Live capture started");
        }
        catch (Exception exception) { SetStatus(exception.Message); }
    }

    private async void StopCapture_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        try
        {
            await _service.StopCaptureAsync();
            StartCaptureButton.IsEnabled = true;
            StopCaptureButton.IsEnabled = false;
            SetStatus("Live capture stopped");
        }
        catch (Exception exception) { SetStatus(exception.Message); }
    }

    private async void ReadParameters_Click(object sender, RoutedEventArgs e) => await ReadParametersAsync(true);

    private async Task ReadParametersAsync(bool showStatus)
    {
        if (_service is null || !_service.IsConnected || _parameterReadPending) return;
        _parameterReadPending = true;
        try
        {
            VisionCameraParameters parameters = await _service.GetParametersAsync();
            if (!ExposureTextBox.IsKeyboardFocused) ExposureTextBox.Text = parameters.Exposure.ToString("0.###", CultureInfo.InvariantCulture);
            if (!GainTextBox.IsKeyboardFocused) GainTextBox.Text = parameters.Gain.ToString("0.###", CultureInfo.InvariantCulture);
            if (showStatus) SetStatus("Camera parameters refreshed");
        }
        catch (Exception exception) { if (showStatus) SetStatus(exception.Message); }
        finally { _parameterReadPending = false; }
    }

    private async void ApplyParameters_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null || !double.TryParse(ExposureTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double exposure) ||
            !double.TryParse(GainTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double gain))
        {
            SetStatus("Enter valid exposure and gain values.");
            return;
        }
        try { await _service.SetParametersAsync(exposure, gain); await ReadParametersAsync(false); SetStatus("Camera parameters applied"); }
        catch (Exception exception) { SetStatus(exception.Message); }
    }

    private void StartInterval_Click(object sender, RoutedEventArgs e)
    {
        if (_service?.IsCapturing != true)
        {
            SetStatus("Start live capture before interval saving.");
            return;
        }
        if (!double.TryParse(IntervalTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || seconds <= 0)
        {
            SetStatus("Enter a positive interval in seconds.");
            return;
        }
        _saveTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, seconds));
        _saveTimer.Start();
        StartIntervalButton.IsEnabled = false;
        StopIntervalButton.IsEnabled = true;
        SetStatus("Interval capture started");
    }

    private void StopInterval_Click(object sender, RoutedEventArgs e)
    {
        _saveTimer.Stop();
        StartIntervalButton.IsEnabled = true;
        StopIntervalButton.IsEnabled = false;
        SetStatus("Interval capture stopped");
    }

    private async void SaveOne_Click(object sender, RoutedEventArgs e) => await SaveLatestImageAsync();

    private async Task SaveLatestImageAsync()
    {
        if (_imageSavePending) return;
        if (_latestImage is null) { SetStatus("Start live capture before saving an image."); return; }
        _imageSavePending = true;
        try
        {
            string directory = SavePathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("Choose an image folder first.");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, $"Camera_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(_latestImage));
            await using FileStream stream = File.Create(file);
            encoder.Save(stream);
            SetStatus($"Saved: {file}");
        }
        catch (Exception exception) { SetStatus(exception.Message); }
        finally { _imageSavePending = false; }
    }

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        using System.Windows.Forms.FolderBrowserDialog dialog = new() { SelectedPath = SavePathTextBox.Text };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) SavePathTextBox.Text = dialog.SelectedPath;
    }

    private void Service_FrameArrived(object? sender, VisionCameraFrameEventArgs e)
    {
        lock (_frameDispatchLock)
        {
            _pendingImage = e.Image;
            if (_frameDispatchPending) return;
            _frameDispatchPending = true;
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            BitmapSource? image;
            lock (_frameDispatchLock)
            {
                image = _pendingImage;
                _pendingImage = null;
                _frameDispatchPending = false;
            }
            if (image is null) return;
            _latestImage = image;
            CameraImage.Source = image;
            ImageHintText.Visibility = Visibility.Collapsed;
        });
    }

    private void Service_CaptureFailed(object? sender, VisionCameraErrorEventArgs e) => Dispatcher.BeginInvoke(async () =>
    {
        if (_service is not null)
        {
            try { await _service.StopCaptureAsync(); }
            catch (Exception exception) { SetStatus(exception.Message); }
        }
        StartCaptureButton.IsEnabled = true;
        StopCaptureButton.IsEnabled = false;
        SetStatus(e.Message);
    });

    private void SetStatus(string text) => OperationStatusText.Text = text;
}
