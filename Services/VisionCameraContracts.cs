using System.Windows.Media.Imaging;

namespace TensileNeW.Services;

public sealed record VisionCameraDevice(string Id, string DisplayName);

public sealed record VisionCameraParameters(double Exposure, double Gain);

public sealed record VisionCameraPreferences(string DeviceId, string DeviceName, string SavePath, double SaveIntervalSeconds = 1);

public sealed class VisionCameraFrameEventArgs(BitmapSource image) : EventArgs
{
    public BitmapSource Image { get; } = image;
}

public sealed class VisionCameraErrorEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public interface IVisionCameraService : IAsyncDisposable
{
    event EventHandler<VisionCameraFrameEventArgs>? FrameArrived;
    event EventHandler<VisionCameraErrorEventArgs>? CaptureFailed;
    bool IsConnected { get; }
    bool IsCapturing { get; }
    Task<IReadOnlyList<VisionCameraDevice>> EnumerateDevicesAsync(CancellationToken cancellationToken = default);
    Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default);
    Task StartCaptureAsync(CancellationToken cancellationToken = default);
    Task StopCaptureAsync(CancellationToken cancellationToken = default);
    Task<VisionCameraParameters> GetParametersAsync(CancellationToken cancellationToken = default);
    Task SetParametersAsync(double exposure, double gain, CancellationToken cancellationToken = default);
}

public interface IVisionCameraServiceFactory
{
    IVisionCameraService Create();
}
