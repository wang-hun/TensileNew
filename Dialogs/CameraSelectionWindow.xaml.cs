using HandyControl.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TensileNeW.Services;
using NLog;

namespace TensileNeW;

public partial class CameraSelectionWindow : UserControl
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly CameraCaptureService _cameraService;
    private int _previewVersion;
    private bool _keepConnectionForOwner;
    private bool _isClosed;

    public event EventHandler<CameraDeviceSelectedEventArgs>? Confirmed;

    public CameraSelectionWindow(
        IReadOnlyList<CameraDeviceDescriptor> devices,
        CameraCaptureService cameraService)
    {
        InitializeComponent();
        _cameraService = cameraService;
        CameraComboBox.ItemsSource = devices;
        CameraComboBox.SelectedIndex = devices.Count > 0 ? 0 : -1;
    }

    public CameraDeviceDescriptor? SelectedDevice => CameraComboBox.SelectedItem as CameraDeviceDescriptor;

    private async void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await StartPreviewAsync(SelectedDevice);
    }

    private async Task StartPreviewAsync(CameraDeviceDescriptor? device)
    {
        int version = ++_previewVersion;
        PreviewImage.Source = null;
        PreviewEmptyTextBlock.Text = device is null ? "未选择摄像头" : "正在连接预览";
        PreviewEmptyTextBlock.Visibility = Visibility.Visible;

        if (device is null || _isClosed)
        {
            return;
        }

        _cameraService.FrameArrived -= PreviewService_FrameArrived;
        _cameraService.CaptureFailed -= PreviewService_CaptureFailed;
        _cameraService.SetBitmapDispatcher(Dispatcher);

        try
        {
            await Task.Run(async () => await _cameraService.StartAsync(device.Id, Dispatcher));
            if (_isClosed || version != _previewVersion)
            {
                return;
            }

            _cameraService.FrameArrived += PreviewService_FrameArrived;
            _cameraService.CaptureFailed += PreviewService_CaptureFailed;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "摄像头预览启动失败。");
            if (!_isClosed && version == _previewVersion)
            {
                PreviewImage.Source = null;
                PreviewEmptyTextBlock.Text = "摄像头预览失败";
                PreviewEmptyTextBlock.Visibility = Visibility.Visible;
            }
        }
    }

    private void PreviewService_FrameArrived(object? sender, CameraFrameArrivedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => PreviewService_FrameArrived(sender, e));
            return;
        }

        PreviewImage.Source = e.Bitmap;
        PreviewEmptyTextBlock.Visibility = e.Bitmap is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PreviewService_CaptureFailed(object? sender, Exception e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            PreviewImage.Source = null;
            PreviewEmptyTextBlock.Text = "摄像头预览失败";
            PreviewEmptyTextBlock.Visibility = Visibility.Visible;
        });
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        CameraDeviceDescriptor? selectedDevice = SelectedDevice;
        if (selectedDevice is null)
        {
            return;
        }

        _keepConnectionForOwner = true;
        DetachPreviewHandlers();
        Confirmed?.Invoke(this, new CameraDeviceSelectedEventArgs(selectedDevice));
        CloseDialog();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CloseDialog();
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_keepConnectionForOwner)
        {
            return;
        }

        StopPreviewInBackground();
    }

    private void DetachPreviewHandlers()
    {
        _cameraService.FrameArrived -= PreviewService_FrameArrived;
        _cameraService.CaptureFailed -= PreviewService_CaptureFailed;
    }

    private void StopPreviewInBackground()
    {
        _isClosed = true;
        DetachPreviewHandlers();
        _ = Task.Run(async () =>
        {
            try
            {
                await _cameraService.StopAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "关闭摄像头预览失败。");
            }
        });
    }

    private void CloseDialog()
    {
        _isClosed = true;

        DependencyObject? current = this;
        while (current != null)
        {
            if (current is Dialog dialog)
            {
                dialog.Close();
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }
}

public sealed class CameraDeviceSelectedEventArgs(CameraDeviceDescriptor device) : EventArgs
{
    public CameraDeviceDescriptor Device { get; } = device;
}
