using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using TensileNeW.Services;

namespace TensileNeW;

public partial class CameraPreviewWindow : Window
{
    private const string DemandKey = "preview-window";

    private CameraCaptureService? _cameraService;

    public CameraPreviewWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Lets this window request a frame size matching its own rendered area. Because
    /// the window is resizable and can be maximised on any monitor, the demand is
    /// whatever it currently occupies - it is never clamped to a fixed resolution.
    /// </summary>
    public void AttachCaptureService(CameraCaptureService? cameraService)
    {
        _cameraService = cameraService;
        ReportDisplayDemand();
    }

    public void SetPreviewSource(BitmapSource? source)
    {
        PreviewImage.Source = source;
        EmptyTextBlock.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReportDisplayDemand()
    {
        CameraCaptureService? cameraService = _cameraService;
        if (cameraService is null)
        {
            return;
        }

        if (PreviewImage.ActualWidth <= 0 || PreviewImage.ActualHeight <= 0)
        {
            return;
        }

        double scaleX = 1.0;
        double scaleY = 1.0;
        PresentationSource source = PresentationSource.FromVisual(PreviewImage);
        if (source?.CompositionTarget is not null)
        {
            scaleX = source.CompositionTarget.TransformToDevice.M11;
            scaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        cameraService.ReportDisplayDemand(
            DemandKey,
            (int)Math.Ceiling(PreviewImage.ActualWidth * scaleX),
            (int)Math.Ceiling(PreviewImage.ActualHeight * scaleY));
    }

    private void PreviewImage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ReportDisplayDemand();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Withdraw the demand so the stream can drop back to the small home preview size.
        _cameraService?.ReportDisplayDemand(DemandKey, 0, 0);
        _cameraService = null;
        base.OnClosed(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close(); 
    }
}
