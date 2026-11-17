using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GxIAPINET;
using NLog;
using TensileNeW.Models;

namespace TensileNeW.Services;

public sealed class DahengCameraSdkServiceFactory : IVisionCameraServiceFactory
{
    public IVisionCameraService Create() => new DahengCameraSdkService();
}

public sealed class DahengCameraSdkService : IVisionCameraService, IVisionCameraNativeImageSaver
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const int ImageWaitMilliseconds = 500;
    private const int ImageTimeoutWarningInterval = 60;
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
    private (string Path, TaskCompletionSource<bool> Completion)? _pendingSave;

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

    public async Task SaveFrameAsync(string filePath, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunOnWorkerAsync(() =>
        {
            if (!_capturing) throw new InvalidOperationException("相机尚未采集图像。");
            if (_pendingSave is not null) throw new InvalidOperationException("相机图片保存尚未完成。");
            _pendingSave = (filePath, completion);
        }, cancellationToken).ConfigureAwait(false);
        await completion.Task.ConfigureAwait(false);
    }
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
        int consecutiveImageTimeouts = 0;
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
                if (!_capturing) consecutiveImageTimeouts = 0;
                continue;
            }
            if (!_capturing) continue;
            try
            {
                if (CaptureOneFrame()) consecutiveImageTimeouts = 0;
            }
            catch (Exception exception)
            {
                if (IsImageWaitTimeout(exception))
                {
                    consecutiveImageTimeouts++;
                    if (consecutiveImageTimeouts == 3 || consecutiveImageTimeouts % ImageTimeoutWarningInterval == 0)
                        Logger.Warn(exception, "大恒相机尚未收到图像，继续等待；设备序列号：{0}；连续等待超时次数：{1}",
                            _connectedDeviceId, consecutiveImageTimeouts);
                    if (consecutiveImageTimeouts >= 3) FailPendingSave(new TimeoutException("等待大恒相机保存帧超时。", exception));
                    continue;
                }
                Logger.Error(exception, "大恒相机实时采集失败；设备序列号：{0}", _connectedDeviceId);
                FailPendingSave(exception);
                consecutiveImageTimeouts = 0;
                try { StopCaptureCore(); }
                catch (Exception stopException)
                {
                    _capturing = false;
                    Logger.Error(stopException, "大恒相机采集失败后停止采集也失败；设备序列号：{0}", _connectedDeviceId);
                }
                try { CaptureFailed?.Invoke(this, new VisionCameraErrorEventArgs(exception.Message)); }
                catch { }
            }
        }
    }

    private static bool IsImageWaitTimeout(Exception exception) =>
        exception.Message.Contains("{-1011}", StringComparison.Ordinal) &&
        exception.Message.Contains("Event wait timeout", StringComparison.OrdinalIgnoreCase);

    private bool CaptureOneFrame()
    {
        IImageData? frame = _stream?.GetImage(ImageWaitMilliseconds);
        if (frame is null) return false;
        try
        {
            if (frame.GetStatus() != GX_FRAME_STATUS_LIST.GX_FRAME_STATUS_SUCCESS)
            {
                FailPendingSave(new InvalidOperationException("相机返回的图像帧无效。"));
                return false;
            }
            int width = checked((int)frame.GetWidth());
            int height = checked((int)frame.GetHeight());
            SavePendingFrame(frame);
            IntPtr source = frame.GetBuffer();
            if (frame.GetPixelFormat() != GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO8)
            {
                _converter!.SetDstFormat(GX_PIXEL_FORMAT_ENTRY.GX_PIXEL_FORMAT_MONO8);
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

            int length = checked(width * height);
            byte[] pixels = new byte[length];
            Marshal.Copy(source, pixels, 0, length);
            BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width);
            image.Freeze();
            FrameArrived?.Invoke(this, new VisionCameraFrameEventArgs(image));
            return true;
        }
        finally { frame.Destroy(); }
    }
    private void SavePendingFrame(IImageData frame)
    {
        if (_pendingSave is not { } request) return;
        _pendingSave = null;
        try
        {
            GX_PIXEL_FORMAT_ENTRY format = frame.GetPixelFormat();
            if (!format.ToString().StartsWith("GX_PIXEL_FORMAT_MONO", StringComparison.Ordinal))
                throw new InvalidOperationException($"相机输出格式 {format} 不是灰度图像。");
            GX_SAVE_IMAGE_INFO saveInfo = new()
            {
                pImageBuffer = frame.GetBuffer(),
                nWidth = checked((uint)frame.GetWidth()),
                nHeight = checked((uint)frame.GetHeight()),
                emSrcFormat = format,
                emCfaMethod = GX_CFA_METHOD.GX_CFA_METHOD_BALANCE,
                emImgFormat = GX_IMAGE_FORMAT_TYPE.GX_IMAGE_FORMAT_BMP,
                pImgPath = request.Path
            };
            Stopwatch? saveClock = RAM.SettingModel?.VisionTimingEnabled != false ? Stopwatch.StartNew() : null;
            _factory!.SaveImage(ref saveInfo);
            if (saveClock is not null)
                Logger.Info("大恒 SDK 灰度帧原生保存耗时 {0:F1}ms，像素格式 {1}。", saveClock.Elapsed.TotalMilliseconds, format);
            request.Completion.TrySetResult(true);
        }
        catch (Exception exception) { request.Completion.TrySetException(exception); }
    }

    private void FailPendingSave(Exception exception)
    {
        if (_pendingSave is not { } request) return;
        _pendingSave = null;
        request.Completion.TrySetException(exception);
    }

    private void StopCaptureCore()
    {
        FailPendingSave(new InvalidOperationException("相机采集已停止。"));
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
