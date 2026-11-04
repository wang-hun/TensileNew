using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TensileNeW.Services;

namespace TensileNeW.Controls;

public partial class VisionCameraPanel : System.Windows.Controls.UserControl
{
    private const double MinimumImageZoom = 0.1;
    private const double MaximumImageZoom = 16;
    private const double ImageZoomStep = 1.15;
    private const double MinimumPreviewHeight = 120;
    private readonly DispatcherTimer _parameterTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _saveTimer = new();
    private IVisionCameraService? _service;
    private BitmapSource? _latestImage;
    private BitmapSource? _latestCapturedImage;
    private readonly object _frameDispatchLock = new();
    private BitmapSource? _pendingImage;
    private bool _frameDispatchPending;
    private bool _parameterReadPending;
    private bool _updatingParameters;
    private bool _parametersEdited;
    private bool _isRefreshingDevices;
    private readonly VisionImageSaveQueue _imageSaveQueue = new();
    private string? _saveQueueFailure;
    private bool _continuousSavingRequested;
    private bool _continuousSavePending;
    private bool _singleSavePending;
    private bool _isReleasing;
    private bool _isLoadingDevices;
    private bool _isConnecting;
    private string? _connectedDeviceId;
    private readonly ScaleTransform _imageScale = new(1, 1);
    private readonly TranslateTransform _imageTranslation = new();
    private double _fitScale = 1;
    private double _userZoom = 1;
    private bool _imageViewAdjusted;
    private Point _dragStartPointer;
    private Point _dragStartTranslation;
    private bool _isDraggingImage;
    private VisionCameraPreferences _preferences = new(string.Empty, string.Empty, string.Empty);

    public VisionCameraPanel()
    {
        InitializeComponent();
        CameraImageCanvas.RenderTransform = new TransformGroup { Children = { _imageScale, _imageTranslation } };
        SavePathTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ECS");
        _parameterTimer.Tick += async (_, _) => await ReadParametersAsync(false);
        _saveTimer.Tick += async (_, _) => await QueueContinuousImageAsync();
    }

    private void PanelLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        FooterScale.MaxHeight = Math.Max(0,
            e.NewSize.Height - PanelLayout.RowDefinitions[0].ActualHeight - MinimumPreviewHeight
            - CameraPreviewBorder.Margin.Bottom);
    }

    public void Configure(IVisionCameraService service, VisionCameraPreferences preferences, Action<VisionCameraPreferences> savePreferences)
    {
        _service = service;
        _preferences = preferences;
        _savePreferences = savePreferences;
        SavePathTextBox.Text = string.IsNullOrWhiteSpace(preferences.SavePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ECS")
            : preferences.SavePath;
        double intervalSeconds = double.IsFinite(preferences.SaveIntervalSeconds) && preferences.SaveIntervalSeconds > 0
            ? preferences.SaveIntervalSeconds
            : 1;
        IntervalTextBox.Text = intervalSeconds.ToString("0.################", CultureInfo.InvariantCulture);
        _service.FrameArrived += Service_FrameArrived;
        _service.CaptureFailed += Service_CaptureFailed;
    }

    private Action<VisionCameraPreferences>? _savePreferences;

    public Task ActivateAsync() => RefreshDevicesAsync(autoConnectSavedDevice: true);

    public async ValueTask ReleaseAsync()
    {
        _parameterTimer.Stop();
        _saveTimer.Stop();
        _isReleasing = true;
        _continuousSavingRequested = false;
        try
        {
            if (_service is not null)
            {
                _service.FrameArrived -= Service_FrameArrived;
                _service.CaptureFailed -= Service_CaptureFailed;
                await _service.DisposeAsync();
                _service = null;
            }
        }
        finally
        {
            await _imageSaveQueue.DisposeAsync();
        }
    }

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync(autoConnectSavedDevice: false);

    private async Task RefreshDevicesAsync(bool autoConnectSavedDevice)
    {
        if (_service is null) return;
        _isRefreshingDevices = true;
        DeviceComboBox.IsEnabled = false;
        RefreshDevicesButton.IsEnabled = false;
        ConnectButton.IsEnabled = false;
        ConnectionStatusText.Text = "正在搜索相机…";
        try
        {
            IReadOnlyList<VisionCameraDevice> devices = await _service.EnumerateDevicesAsync();
            _isLoadingDevices = true;
            DeviceComboBox.ItemsSource = devices;
            VisionCameraDevice? preferredDevice = devices.FirstOrDefault(device =>
                string.Equals(device.Id, _preferences.DeviceId, StringComparison.Ordinal));
            if (preferredDevice is null && !string.IsNullOrWhiteSpace(_preferences.DeviceName))
            {
                preferredDevice = devices.FirstOrDefault(device =>
                    string.Equals(device.DisplayName, _preferences.DeviceName, StringComparison.Ordinal));
            }
            DeviceComboBox.SelectedItem = preferredDevice ?? devices.FirstOrDefault();
            _isLoadingDevices = false;
            if (devices.Count == 0)
            {
                ConnectionStatusText.Text = "未发现相机";
                ImageHintText.Text = "未发现相机，请检查相机供电和连接后点击“刷新设备”。";
                return;
            }

            if (preferredDevice is null)
            {
                ConnectionStatusText.Text = "请选择相机并连接";
                ImageHintText.Text = "已发现相机，请确认上方选中的设备并点击“连接相机”。";
                return;
            }

            if (autoConnectSavedDevice)
            {
                await ConnectSelectedDeviceAsync(isAutomatic: true);
            }
            else
            {
                bool alreadyConnected = _service.IsConnected && string.Equals(preferredDevice.Id, _connectedDeviceId, StringComparison.Ordinal);
                ConnectionStatusText.Text = alreadyConnected ? $"已连接：{preferredDevice.DisplayName}" : "已找到上次使用的相机";
                ImageHintText.Text = alreadyConnected
                    ? "相机已连接，可开始实时采集。"
                    : "找到上次使用的相机，点击“连接相机”即可开始使用。";
            }
        }
        catch (Exception exception)
        {
            _isLoadingDevices = false;
            ConnectionStatusText.Text = "搜索相机失败";
            SetStatus($"搜索相机失败：{exception.Message}");
        }
        finally
        {
            DeviceComboBox.IsEnabled = true;
            RefreshDevicesButton.IsEnabled = true;
            _isRefreshingDevices = false;
            _isConnecting = false;
            UpdateControlAvailability();
        }
    }

    private void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingDevices || DeviceComboBox.SelectedItem is not VisionCameraDevice device) return;
        SavePreferences(device);
        bool switching = _service?.IsConnected == true && !string.Equals(_connectedDeviceId, device.Id, StringComparison.Ordinal);
        ConnectButton.Content = switching ? "切换并连接" : "连接相机";
        ConnectionStatusText.Text = switching
            ? $"当前连接：{GetConnectedDeviceName()}；已选择：{device.DisplayName}"
            : "已选择相机，可连接设备";
        ImageHintText.Text = switching
            ? $"点击“切换并连接”以切换到 {device.DisplayName}。"
            : $"已选择 {device.DisplayName}，连接后即可开始采集。";
        UpdateControlAvailability();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectSelectedDeviceAsync(isAutomatic: false);

    private async Task ConnectSelectedDeviceAsync(bool isAutomatic)
    {
        if (_isReleasing || _service is null || DeviceComboBox.SelectedItem is not VisionCameraDevice device) return;
        _isConnecting = true;
        DeviceComboBox.IsEnabled = false;
        RefreshDevicesButton.IsEnabled = false;
        ConnectButton.IsEnabled = false;
        ConnectionStatusText.Text = isAutomatic ? "正在自动连接…" : _service.IsConnected ? "正在切换相机…" : "正在连接相机…";
        try
        {
            bool switchingDevice = _service.IsConnected && !string.Equals(_connectedDeviceId, device.Id, StringComparison.Ordinal);
            if (switchingDevice)
            {
                _saveTimer.Stop();
                _continuousSavingRequested = false;
                _latestImage = null;
                lock (_frameDispatchLock) _latestCapturedImage = null;
                CameraImage.Source = null;
                ImageHintText.Text = $"正在切换到 {device.DisplayName}…";
                ImageHintPanel.Visibility = Visibility.Visible;
            }
            await _service.ConnectAsync(device.Id);
            _connectedDeviceId = device.Id;
            _parametersEdited = false;
            ConnectionStatusText.Text = $"已连接：{device.DisplayName}";
            ConnectButton.Content = "已连接";
            SavePreferences(device);
            await ReadParametersAsync(true);
            _parameterTimer.Start();
            UpdateControlAvailability();
            ImageHintText.Text = "相机已连接，点击“开始实时采集”显示实时画面。";
            ImageHintPanel.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            if (!_service.IsConnected) _connectedDeviceId = null;
            ConnectionStatusText.Text = "连接失败";
            ImageHintText.Text = isAutomatic
                ? "上次使用的相机暂不可用，请检查设备后点击“连接相机”重试。"
                : "相机连接失败，请确认设备后重试。";
            ImageHintPanel.Visibility = Visibility.Visible;
            SetStatus($"{(isAutomatic ? "自动连接" : "连接相机")}失败：{exception.Message}");
        }
        finally
        {
            _isConnecting = false;
            DeviceComboBox.IsEnabled = true;
            RefreshDevicesButton.IsEnabled = true;
            UpdateControlAvailability();
        }
    }

    private string GetConnectedDeviceName() => DeviceComboBox.Items
        .OfType<VisionCameraDevice>()
        .FirstOrDefault(device => string.Equals(device.Id, _connectedDeviceId, StringComparison.Ordinal))?.DisplayName ?? "其他相机";

    private void UpdateControlAvailability()
    {
        bool connected = _service?.IsConnected == true;
        bool capturing = _service?.IsCapturing == true;
        bool selectedConnectedDevice = DeviceComboBox.SelectedItem is VisionCameraDevice selected &&
            string.Equals(selected.Id, _connectedDeviceId, StringComparison.Ordinal);
        ConnectButton.IsEnabled = !_isConnecting && !_isRefreshingDevices && DeviceComboBox.SelectedItem is VisionCameraDevice && !(connected && selectedConnectedDevice);
        ConnectButton.Content = connected && selectedConnectedDevice ? "已连接" : connected ? "切换并连接" : "连接相机";
        ExposureTextBox.IsEnabled = connected;
        GainTextBox.IsEnabled = connected;
        ApplyParametersButton.IsEnabled = connected && _parametersEdited &&
            double.TryParse(ExposureTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double exposure) && double.IsFinite(exposure) &&
            double.TryParse(GainTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double gain) && double.IsFinite(gain);
        StartCaptureButton.IsEnabled = connected && !capturing;
        StopCaptureButton.IsEnabled = capturing;
        StartIntervalButton.IsEnabled = capturing && !_continuousSavingRequested && !_continuousSavePending;
        StopIntervalButton.IsEnabled = _continuousSavingRequested;
        SaveOneButton.IsEnabled = capturing && GetLatestCapturedImage() is not null && !_singleSavePending;
        RefreshDevicesButton.IsEnabled = !_isRefreshingDevices && !_isConnecting;
        DeviceComboBox.IsEnabled = !_isRefreshingDevices && !_isConnecting;
    }

    private void SavePreferences(VisionCameraDevice device)
    {
        _preferences = _preferences with
        {
            DeviceId = device.Id,
            DeviceName = device.DisplayName,
            SavePath = SavePathTextBox.Text.Trim()
        };
        _savePreferences?.Invoke(_preferences);
    }
    private async void StartCapture_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        lock (_frameDispatchLock) _latestCapturedImage = null;
        try
        {
            await _service.StartCaptureAsync();
            StartCaptureButton.IsEnabled = false;
            StopCaptureButton.IsEnabled = true;
            ImageHintPanel.Visibility = Visibility.Collapsed;
            SetStatus("实时采集已开始");
            UpdateControlAvailability();
        }
        catch (Exception exception) { SetStatus($"操作失败：{exception.Message}"); }
    }

    private async void StopCapture_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        _continuousSavingRequested = false;
        _saveTimer.Stop();
        try
        {
            await _service.StopCaptureAsync();
            SetStatus("实时采集已结束");
            UpdateControlAvailability();
        }
        catch (Exception exception) { SetStatus($"操作失败：{exception.Message}"); }
    }

    private async Task ReadParametersAsync(bool showStatus)
    {
        if (_service is null || !_service.IsConnected || _parameterReadPending) return;
        _parameterReadPending = true;
        try
        {
            VisionCameraParameters parameters = await _service.GetParametersAsync();
            if (!_parametersEdited && !ExposureTextBox.IsKeyboardFocused && !GainTextBox.IsKeyboardFocused)
            {
                _updatingParameters = true;
                ExposureTextBox.Text = parameters.Exposure.ToString("0.###", CultureInfo.InvariantCulture);
                GainTextBox.Text = parameters.Gain.ToString("0.###", CultureInfo.InvariantCulture);
                _updatingParameters = false;
            }
            if (showStatus) SetStatus("相机参数已更新");
        }
        catch (Exception exception) { if (showStatus) SetStatus($"读取相机参数失败：{exception.Message}"); }
        finally { _parameterReadPending = false; }
    }

    private void ParameterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingParameters) return;
        _parametersEdited = true;
        UpdateControlAvailability();
    }

    private async void ApplyParameters_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null || !double.TryParse(ExposureTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double exposure) ||
            !double.TryParse(GainTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double gain) || !double.IsFinite(exposure) || !double.IsFinite(gain))
        {
            SetStatus("请输入有效的曝光和增益数值。");
            return;
        }
        try
        {
            await _service.SetParametersAsync(exposure, gain);
            _parametersEdited = false;
            await ReadParametersAsync(false);
            UpdateControlAvailability();
            SetStatus("相机参数已应用");
        }
        catch (Exception exception) { SetStatus($"操作失败：{exception.Message}"); }
    }

    private void StartInterval_Click(object sender, RoutedEventArgs e)
    {
        if (_service?.IsCapturing != true)
        {
            SetStatus("请先开始实时采集，再进行连续保存。");
            return;
        }
        if (!double.TryParse(IntervalTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || !double.IsFinite(seconds) || seconds <= 0 || seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            SetStatus("请输入大于 0 的保存间隔秒数。");
            return;
        }
        _saveQueueFailure = null;
        PersistSaveInterval(seconds);
        _continuousSavingRequested = true;
        _saveTimer.Interval = TimeSpan.FromSeconds(seconds);
        _saveTimer.Start();
        UpdateControlAvailability();
        SetSaveStatus("连续保存已开始");
    }

    private void IntervalTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(IntervalTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) &&
            double.IsFinite(seconds) && seconds > 0 && seconds <= TimeSpan.MaxValue.TotalSeconds)
        {
            PersistSaveInterval(seconds);
        }
    }

    private void PersistSaveInterval(double seconds)
    {
        _preferences = _preferences with { SaveIntervalSeconds = seconds };
        _savePreferences?.Invoke(_preferences);
    }

    private void StopInterval_Click(object sender, RoutedEventArgs e)
    {
        _continuousSavingRequested = false;
        _saveTimer.Stop();
        UpdateControlAvailability();
        SetSaveStatus("连续保存已结束");
    }

    private async void SaveOne_Click(object sender, RoutedEventArgs e) => await QueueSingleImageAsync();

    private async Task QueueSingleImageAsync()
    {
        if (_isReleasing || _singleSavePending) return;
        BitmapSource? latestImage = GetLatestCapturedImage();
        if (latestImage is null) { SetSaveStatus("等待相机图像后才能保存。"); return; }
        string directory = SavePathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(directory)) { SetSaveStatus("请先选择图片保存路径。"); return; }

        BitmapSource image = latestImage;
        _singleSavePending = true;
        UpdateControlAvailability();
        try
        {
            Task<string> completion = await _imageSaveQueue.QueueAsync(image, directory);
            string file = await completion;
            if (_saveQueueFailure is null) SetSaveStatus($"图片已保存：{Path.GetFileName(file)}");
        }
        catch (Exception exception)
        {
            _continuousSavingRequested = false;
            _saveTimer.Stop();
            _saveQueueFailure = $"保存失败，连续保存已停止：{exception.Message}";
            SetSaveStatus(_saveQueueFailure);
        }
        finally
        {
            _singleSavePending = false;
            UpdateControlAvailability();
        }
    }

    private async Task QueueContinuousImageAsync()
    {
        if (_isReleasing || !_continuousSavingRequested || _continuousSavePending) return;
        BitmapSource? latestImage = GetLatestCapturedImage();
        if (latestImage is null)
        {
            _continuousSavingRequested = false;
            _saveTimer.Stop();
            SetSaveStatus("连续保存已停止：尚未收到相机图像。");
            UpdateControlAvailability();
            return;
        }

        string directory = SavePathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(directory))
        {
            _continuousSavingRequested = false;
            _saveTimer.Stop();
            SetSaveStatus("连续保存已停止：请先选择图片保存路径。");
            UpdateControlAvailability();
            return;
        }

        BitmapSource image = latestImage;
        _continuousSavePending = true;
        _saveTimer.Stop();
        UpdateControlAvailability();
        try
        {
            Task<string> completion = await _imageSaveQueue.QueueAsync(image, directory);
            SetSaveStatus("图片已加入后台保存队列。");
            _ = ObserveQueuedSaveAsync(completion);
        }
        catch (Exception exception)
        {
            _continuousSavingRequested = false;
            _saveQueueFailure = $"连续保存已停止：{exception.Message}";
            SetSaveStatus(_saveQueueFailure);
        }
        finally
        {
            _continuousSavePending = false;
            if (_continuousSavingRequested && _service?.IsCapturing == true) _saveTimer.Start();
            UpdateControlAvailability();
        }
    }

    private async Task ObserveQueuedSaveAsync(Task<string> completion)
    {
        try
        {
            string file = await completion;
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (_saveQueueFailure is null) SetSaveStatus($"图片已保存：{Path.GetFileName(file)}");
            });
        }
        catch (Exception exception)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            await Dispatcher.InvokeAsync(() =>
            {
                _continuousSavingRequested = false;
                _saveTimer.Stop();
                UpdateControlAvailability();
                _saveQueueFailure = $"保存失败，连续保存已停止：{exception.Message}";
                SetSaveStatus(_saveQueueFailure);
            });
        }
    }

    private async void OpenSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        string directory = SavePathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(directory)) { SetSaveStatus("请先选择图片保存路径。"); return; }
        try
        {
            await Task.Run(() => Directory.CreateDirectory(directory));
            await Task.Run(() => Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true }));
        }
        catch (Exception exception) { SetSaveStatus($"打开图片文件夹失败：{exception.Message}"); }
    }

    private void SavePathTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _preferences = _preferences with { SavePath = SavePathTextBox.Text.Trim() };
        _savePreferences?.Invoke(_preferences);
    }

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        using System.Windows.Forms.FolderBrowserDialog dialog = new() { SelectedPath = SavePathTextBox.Text, Description = "选择相机图片保存文件夹" };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SavePathTextBox.Text = dialog.SelectedPath;
            if (DeviceComboBox.SelectedItem is VisionCameraDevice device) SavePreferences(device);
            else
            {
                _preferences = _preferences with { SavePath = dialog.SelectedPath };
                _savePreferences?.Invoke(_preferences);
            }
        }
    }

    private void Service_FrameArrived(object? sender, VisionCameraFrameEventArgs e)
    {
        if (_isReleasing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        lock (_frameDispatchLock)
        {
            _latestCapturedImage = e.Image;
            _pendingImage = e.Image;
            if (_frameDispatchPending) return;
            _frameDispatchPending = true;
        }

        try
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            {
                BitmapSource? image;
                lock (_frameDispatchLock)
                {
                    image = _pendingImage;
                    _pendingImage = null;
                    _frameDispatchPending = false;
                }
                if (image is null || _isReleasing) return;
                _latestImage = image;
                CameraImage.Source = image;
                if (CameraImageCanvas.Width != image.Width || CameraImageCanvas.Height != image.Height)
                {
                    CameraImageCanvas.Width = image.Width;
                    CameraImageCanvas.Height = image.Height;
                    CameraImage.Width = image.Width;
                    CameraImage.Height = image.Height;
                    _imageViewAdjusted = false;
                }
                ResetImageViewButton.IsEnabled = true;
                if (!_imageViewAdjusted) ResetImageView();
                ImageHintPanel.Visibility = Visibility.Collapsed;
                UpdateControlAvailability();
            });
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            lock (_frameDispatchLock)
            {
                _pendingImage = null;
                _frameDispatchPending = false;
            }
        }
    }

    private void CameraViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (CameraImage.Source is null) return;
        Point pointer = e.GetPosition(CameraViewport);
        double factor = e.Delta > 0 ? ImageZoomStep : 1 / ImageZoomStep;
        double zoom = Math.Clamp(_userZoom * factor, MinimumImageZoom, MaximumImageZoom);
        factor = zoom / _userZoom;
        _userZoom = zoom;
        _imageViewAdjusted = true;
        _imageTranslation.X = pointer.X - (pointer.X - _imageTranslation.X) * factor;
        _imageTranslation.Y = pointer.Y - (pointer.Y - _imageTranslation.Y) * factor;
        _imageScale.ScaleX = _fitScale * _userZoom;
        _imageScale.ScaleY = _fitScale * _userZoom;
        ConstrainImageTranslation();
        e.Handled = true;
    }

    private void CameraViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (CameraImage.Source is null) return;
        if (ResetImageViewButton.IsMouseOver) return;
        _isDraggingImage = true;
        _dragStartPointer = e.GetPosition(CameraViewport);
        _dragStartTranslation = new Point(_imageTranslation.X, _imageTranslation.Y);
        _imageViewAdjusted = true;
        CameraViewport.Cursor = Cursors.SizeAll;
        CameraViewport.CaptureMouse();
        e.Handled = true;
    }

    private void CameraViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingImage) return;
        Point pointer = e.GetPosition(CameraViewport);
        _imageTranslation.X = _dragStartTranslation.X + pointer.X - _dragStartPointer.X;
        _imageTranslation.Y = _dragStartTranslation.Y + pointer.Y - _dragStartPointer.Y;
        ConstrainImageTranslation();
    }

    private void CameraViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingImage) return;
        _isDraggingImage = false;
        CameraViewport.ReleaseMouseCapture();
        CameraViewport.Cursor = Cursors.Arrow;
    }

    private void CameraViewport_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _isDraggingImage = false;
        CameraViewport.Cursor = Cursors.Arrow;
    }

    private void CameraViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_imageViewAdjusted) ResetImageView();
        else ConstrainImageTranslation();
    }

    private void ResetImageView_Click(object sender, RoutedEventArgs e) => ResetImageView();

    private void ResetImageView()
    {
        if (CameraImage.Source is not BitmapSource image || CameraViewport.ActualWidth <= 0 || CameraViewport.ActualHeight <= 0) return;
        _fitScale = Math.Min(CameraViewport.ActualWidth / image.Width, CameraViewport.ActualHeight / image.Height);
        if (!double.IsFinite(_fitScale) || _fitScale <= 0) _fitScale = 1;
        _userZoom = 1;
        _imageScale.ScaleX = _fitScale;
        _imageScale.ScaleY = _fitScale;
        _imageTranslation.X = (CameraViewport.ActualWidth - image.Width * _fitScale) / 2;
        _imageTranslation.Y = (CameraViewport.ActualHeight - image.Height * _fitScale) / 2;
        _imageViewAdjusted = false;
    }
    private void ConstrainImageTranslation()
    {
        if (CameraImage.Source is null || CameraViewport.ActualWidth <= 0 || CameraViewport.ActualHeight <= 0) return;
        double scaledWidth = CameraImageCanvas.Width * _imageScale.ScaleX;
        double scaledHeight = CameraImageCanvas.Height * _imageScale.ScaleY;
        _imageTranslation.X = scaledWidth <= CameraViewport.ActualWidth
            ? (CameraViewport.ActualWidth - scaledWidth) / 2
            : Math.Clamp(_imageTranslation.X, CameraViewport.ActualWidth - scaledWidth, 0);
        _imageTranslation.Y = scaledHeight <= CameraViewport.ActualHeight
            ? (CameraViewport.ActualHeight - scaledHeight) / 2
            : Math.Clamp(_imageTranslation.Y, CameraViewport.ActualHeight - scaledHeight, 0);
    }

    private void Service_CaptureFailed(object? sender, VisionCameraErrorEventArgs e) => Dispatcher.BeginInvoke(async () =>
    {
        _continuousSavingRequested = false;
        _saveTimer.Stop();
        if (_service is not null)
        {
            try { await _service.StopCaptureAsync(); }
            catch (Exception exception) { SetStatus($"操作失败：{exception.Message}"); }
        }
        UpdateControlAvailability();
        ImageHintText.Text = $"实时采集已停止：{e.Message}";
        ImageHintPanel.Visibility = Visibility.Visible;
        SetStatus($"相机采集失败：{e.Message}");
    });

    private BitmapSource? GetLatestCapturedImage()
    {
        lock (_frameDispatchLock) return _latestCapturedImage;
    }

    private void SetStatus(string text) => OperationStatusText.Text = text;
    private void SetSaveStatus(string text) => SaveStatusText.Text = text;
}
