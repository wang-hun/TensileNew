using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GxIAPINET;

namespace TensileNeW.Services;

public sealed class DahengCameraSdkServiceFactory : IVisionCameraServiceFactory
{
    public IVisionCameraService Create() => new DahengCameraSdkService();
}

public sealed class DahengCameraSdkService : IVisionCameraService
{
    private readonly BlockingCollection<Action> _commands = new();
    private readonly Thread _workerThread;
    private readonly ManualResetEventSlim _initializationCompleted = new();
    private IGXFactory? _factory;
    private IGXDevice? _device;
    private IGXStream? _stream;
    private IGXFeatureControl? _features;
    private IGXImageFormatConvert? _converter;
    private IntPtr _conversionBuffer;
    private ulong _conversionCapacity;
    private volatile bool _connected;
    private volatile bool _capturing;
    private volatile bool _disposed;
    private Exception? _initializationError;
    private string? _connectedDeviceId;

    public event EventHandler<VisionCameraFrameEventArgs>? FrameArrived;
    public event EventHandler<VisionCameraErrorEventArgs>? CaptureFailed;
    public bool IsConnected => _connected;
    public bool IsCapturing => _capturing;

    public DahengCameraSdkService()
    {
        _workerThread = new Thread(WorkerLoop) { IsBackground = true, Name = "Daheng camera worker" };
        _workerThread.Start();
        _initializationCompleted.Wait();
        _initializationCompleted.Dispose();
        if (_initializationError is not null)
        {
            _commands.CompleteAdding();
            _workerThread.Join();
            _commands.Dispose();
            throw new InvalidOperationException("大恒 SDK 初始化失败。", _initializationError);
        }
    }

    public Task<IReadOnlyList<VisionCameraDevice>> EnumerateDevicesAsync(CancellationToken cancellationToken = default) =>
        RunOnWorkerAsync(EnumerateDevicesCore, cancellationToken);

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default) => RunOnWorkerAsync(() =>
    {
        if (_connected && string.Equals(_connectedDeviceId, deviceId, StringComparison.Ordinal)) return;
        CloseDevice();
        List<IGXDeviceInfo> devices = GetDeviceInfos();
        IGXDeviceInfo selected = devices.FirstOrDefault(info =>
            string.Equals(info.GetSN(), deviceId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("未找到所选相机，请刷新设备列表。");
        try
        {
            _device = _factory!.OpenDeviceBySN(selected.GetSN(), GX_ACCESS_MODE.GX_ACCESS_EXCLUSIVE);
            _features = _device.GetRemoteFeatureControl();
            _stream = _device.OpenStream(0);
            _converter = _factory!.CreateImageFormatConvert();
            if (_features.IsImplemented("TriggerMode"))
                _features.GetEnumFeature("TriggerMode").SetValue("Off");
            if (_features.IsImplemented("AcquisitionMode"))
                _features.GetEnumFeature("AcquisitionMode").SetValue("Continuous");
            _connectedDeviceId = deviceId;
            _connected = true;
        }
        catch
        {
            CloseDevice();
            throw;
        }
    }, cancellationToken);

    public Task StartCaptureAsync(CancellationToken cancellationToken = default) => RunOnWorkerAsync(() =>
    {
        if (!_connected || _stream is null || _features is null)
            throw new InvalidOperationException("请先连接相机。");
        if (_capturing) return;
        _stream.StartGrab();
        try
        {
            _features.GetCommandFeature("AcquisitionStart").Execute();
            _capturing = true;
        }
        catch
        {
            _stream.StopGrab();
            throw;
        }
    }, cancellationToken);

    public Task StopCaptureAsync(CancellationToken cancellationToken = default) =>
        RunOnWorkerAsync(StopCaptureCore, cancellationToken);

    public Task<VisionCameraParameters> GetParametersAsync(CancellationToken cancellationToken = default) => RunOnWorkerAsync(() =>
    {
        if (!_connected || _features is null) throw new InvalidOperationException("相机尚未连接。");
        return new VisionCameraParameters(
            _features.GetFloatFeature("ExposureTime").GetValue(),
            _features.GetFloatFeature("Gain").GetValue());
    }, cancellationToken);

    public Task SetParametersAsync(double exposure, double gain, CancellationToken cancellationToken = default) => RunOnWorkerAsync(() =>
    {
        if (!_connected || _features is null) throw new InvalidOperationException("相机尚未连接。");
        _features.GetFloatFeature("ExposureTime").SetValue(exposure);
        _features.GetFloatFeature("Gain").SetValue(gain);
    }, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try
        {
            await RunOnWorkerAsync(() =>
            {
                try { CloseDevice(); }
                finally { _factory?.Uninit(); }
            }).ConfigureAwait(false);
        }
        finally
        {
            _disposed = true;
            _commands.CompleteAdding();
            await Task.Run(_workerThread.Join).ConfigureAwait(false);
            _commands.Dispose();
        }
    }

    private List<IGXDeviceInfo> GetDeviceInfos()
    {
        List<IGXDeviceInfo> devices = new();
        _factory!.UpdateAllDeviceList(200, devices);
        return devices;
    }

    private IReadOnlyList<VisionCameraDevice> EnumerateDevicesCore() => GetDeviceInfos()
        .Select(info => new VisionCameraDevice(info.GetSN(), $"{info.GetModelName()}（序列号：{info.GetSN()}）"))
        .ToArray();

    private void WorkerLoop()
    {
        try
        {
            _factory = IGXFactory.GetInstance();
            _factory.Init();
        }
        catch (Exception exception) { _initializationError = exception; }
        finally { _initializationCompleted.Set(); }
        while (!_commands.IsCompleted)
        {
            if (_commands.TryTake(out Action? command, _capturing ? 0 : Timeout.Infinite))
            {
                command();
                continue;
            }
            if (!_capturing) continue;
            try { CaptureOneFrame(); }
            catch (Exception exception)
            {
                try { StopCaptureCore(); }
                catch { _capturing = false; }
                try { CaptureFailed?.Invoke(this, new VisionCameraErrorEventArgs(exception.Message)); }
                catch { }
            }
        }
    }

    private void CaptureOneFrame()
    {
        IImageData? frame = _stream?.GetImage(50);
        if (frame is null) return;
        try
        {
            if (frame.GetStatus() != GX_FRAME_STATUS_LIST.GX_FRAME_STATUS_SUCCESS) return;
            int width = checked((int)frame.GetWidth());
            int height = checked((int)frame.GetHeight());
            int length = checked(width * height * 3);
            IntPtr source = frame.GetBuffer();
            if (frame.GetPixelFormat() != GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BGR8)
            {
                _converter!.SetDstFormat(GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_BGR8);
                ulong required = _converter.GetBufferSizeForConversion(frame);
                if (required > _conversionCapacity)
                {
                    if (_conversionBuffer != IntPtr.Zero) Marshal.FreeCoTaskMem(_conversionBuffer);
                    _conversionBuffer = Marshal.AllocCoTaskMem(checked((int)required));
                    _conversionCapacity = required;
                }
                _converter.Convert(frame, _conversionBuffer, required, false);
                source = _conversionBuffer;
            }
            byte[] pixels = new byte[length];
            Marshal.Copy(source, pixels, 0, length);
            BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, width * 3);
            image.Freeze();
            FrameArrived?.Invoke(this, new VisionCameraFrameEventArgs(image));
        }
        finally { frame.Destroy(); }
    }

    private void StopCaptureCore()
    {
        if (!_capturing) return;
        _capturing = false;
        try { _features?.GetCommandFeature("AcquisitionStop").Execute(); }
        finally { _stream?.StopGrab(); }
    }

    private void CloseDevice()
    {
        try { StopCaptureCore(); }
        finally
        {
            try { _stream?.Close(); }
            finally
            {
                _stream = null;
                try { _device?.Close(); }
                finally
                {
                    _device = null;
                    _features = null;
                    _converter = null;
                    _connected = false;
                    _connectedDeviceId = null;
                    if (_conversionBuffer != IntPtr.Zero) Marshal.FreeCoTaskMem(_conversionBuffer);
                    _conversionBuffer = IntPtr.Zero;
                    _conversionCapacity = 0;
                }
            }
        }
    }

    private Task RunOnWorkerAsync(Action operation, CancellationToken cancellationToken = default) =>
        RunOnWorkerAsync(() => { operation(); return true; }, cancellationToken);

    private Task<T> RunOnWorkerAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.FromException<T>(new ObjectDisposedException(nameof(DahengCameraSdkService)));
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _commands.Add(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }
                try
                {
                    if (_initializationError is not null) throw new InvalidOperationException("大恒 SDK 初始化失败。", _initializationError);
                    completion.TrySetResult(operation());
                }
                catch (Exception exception) { completion.TrySetException(exception); }
            });
        }
        catch (Exception exception) { completion.TrySetException(exception); }
        return completion.Task;
    }
}
