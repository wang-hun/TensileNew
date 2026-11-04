using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MVSDK_Net;

namespace TensileNeW.Services;

public sealed class MvsCameraSdkServiceFactory : IVisionCameraServiceFactory
{
    public IVisionCameraService Create() => new MvsCameraSdkService();
}

public sealed class MvsCameraSdkService : IVisionCameraService
{
    private readonly BlockingCollection<Action> _commands = new();
    private readonly MyCamera _camera = new();
    private readonly Thread _workerThread;
    private volatile bool _connected;
    private volatile bool _capturing;
    private volatile bool _disposed;
    private bool _handle;
    private bool _grabberStarted;
    private string? _connectedDeviceId;

    public event EventHandler<VisionCameraFrameEventArgs>? FrameArrived;
    public event EventHandler<VisionCameraErrorEventArgs>? CaptureFailed;
    public bool IsConnected => _connected;
    public bool IsCapturing => _capturing;

    public MvsCameraSdkService()
    {
        _workerThread = new Thread(CameraWorkerLoop)
        {
            IsBackground = true,
            Name = "MVS camera worker"
        };
        _workerThread.Start();
    }

    public Task<IReadOnlyList<VisionCameraDevice>> EnumerateDevicesAsync(CancellationToken ct = default) =>
        RunOnWorkerAsync(EnumerateDevicesCore, ct);

    public Task ConnectAsync(string deviceId, CancellationToken ct = default) => RunOnWorkerAsync(() =>
    {
        if (_capturing && !string.Equals(_connectedDeviceId, deviceId, StringComparison.Ordinal)) StopCaptureCore();
        if (_connected && string.Equals(_connectedDeviceId, deviceId, StringComparison.Ordinal)) return;
        if (_connected)
        {
            Check(_camera.IMV_Close(), "断开相机");
            _connected = false;
            _connectedDeviceId = null;
        }
        if (_handle)
        {
            Check(_camera.IMV_DestroyHandle(), "释放相机句柄");
            _handle = false;
        }

        IReadOnlyList<VisionCameraDevice> devices = EnumerateDevicesCore();
        int selectedIndex = -1;
        for (int index = 0; index < devices.Count; index++)
        {
            if (string.Equals(devices[index].Id, deviceId, StringComparison.Ordinal))
            {
                selectedIndex = index;
                break;
            }
        }
        if (selectedIndex < 0) throw new InvalidOperationException("未找到所选相机，请刷新设备列表。");

        Check(_camera.IMV_CreateHandle(IMVDefine.IMV_ECreateHandleMode.modeByIndex, selectedIndex), "创建相机句柄");
        _handle = true;
        try
        {
            Check(_camera.IMV_Open(), "连接相机");
            _connected = true;
            _connectedDeviceId = deviceId;
        }
        catch
        {
            _camera.IMV_DestroyHandle();
            _handle = false;
            throw;
        }
    }, ct);

    public Task StartCaptureAsync(CancellationToken ct = default) => RunOnWorkerAsync(() =>
    {
        if (!_connected) throw new InvalidOperationException("请先连接相机。");
        if (_capturing) return;
        Check(_camera.IMV_StartGrabbing(), "开始采集");
        _grabberStarted = true;
        _capturing = true;
    }, ct);

    public Task StopCaptureAsync(CancellationToken ct = default) => RunOnWorkerAsync(StopCaptureCore, ct);

    public Task<VisionCameraParameters> GetParametersAsync(CancellationToken ct = default) => RunOnWorkerAsync(() =>
    {
        if (!_connected) throw new InvalidOperationException("相机尚未连接。");
        double exposure = 0;
        double gain = 0;
        Check(_camera.IMV_GetDoubleFeatureValue("ExposureTime", ref exposure), "读取曝光");
        Check(_camera.IMV_GetDoubleFeatureValue("GainRaw", ref gain), "读取增益");
        return new VisionCameraParameters(exposure, gain);
    }, ct);

    public Task SetParametersAsync(double exposure, double gain, CancellationToken ct = default) => RunOnWorkerAsync(() =>
    {
        if (!_connected) throw new InvalidOperationException("相机尚未连接。");
        Check(_camera.IMV_SetDoubleFeatureValue("ExposureTime", exposure), "设置曝光");
        Check(_camera.IMV_SetDoubleFeatureValue("GainRaw", gain), "设置增益");
    }, ct);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try
        {
            await RunOnWorkerAsync(() =>
            {
                StopCaptureCore();
                if (_connected) Check(_camera.IMV_Close(), "断开相机");
                _connected = false;
                _connectedDeviceId = null;
                if (_handle) Check(_camera.IMV_DestroyHandle(), "释放相机句柄");
                _handle = false;
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

    private Task RunOnWorkerAsync(Action operation, CancellationToken ct = default) =>
        RunOnWorkerAsync(() =>
        {
            operation();
            return true;
        }, ct);

    private Task<T> RunOnWorkerAsync<T>(Func<T> operation, CancellationToken ct = default)
    {
        if (_disposed) return Task.FromException<T>(new ObjectDisposedException(nameof(MvsCameraSdkService)));
        ct.ThrowIfCancellationRequested();
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _commands.Add(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    completion.TrySetCanceled(ct);
                    return;
                }
                try
                {
                    completion.TrySetResult(operation());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        return completion.Task;
    }

    private void CameraWorkerLoop()
    {
        IntPtr buffer = IntPtr.Zero;
        int capacity = 0;
        try
        {
            while (true)
            {
                if (_commands.TryTake(out Action? command))
                {
                    command();
                    continue;
                }
                if (_commands.IsCompleted) break;
                if (!_capturing)
                {
                    try
                    {
                        command = _commands.Take();
                        command();
                    }
                    catch (InvalidOperationException) when (_commands.IsCompleted)
                    {
                        break;
                    }
                    continue;
                }

                try
                {
                    CaptureOneFrame(ref buffer, ref capacity);
                }
                catch (Exception exception)
                {
                    _capturing = false;
                    try { CaptureFailed?.Invoke(this, new VisionCameraErrorEventArgs(exception.Message)); }
                    catch { }
                }
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    private IReadOnlyList<VisionCameraDevice> EnumerateDevicesCore()
    {
        IMVDefine.IMV_DeviceList list = new();
        Check(MyCamera.IMV_EnumDevices(ref list, (uint)IMVDefine.IMV_EInterfaceType.interfaceTypeAll), "枚举相机");
        List<VisionCameraDevice> result = new();
        int size = Marshal.SizeOf(typeof(IMVDefine.IMV_DeviceInfo));
        for (int index = 0; index < list.nDevNum; index++)
        {
            IMVDefine.IMV_DeviceInfo info = Marshal.PtrToStructure<IMVDefine.IMV_DeviceInfo>(list.pDevInfo + index * size);
            string serialNumber = info.serialNumber?.Trim() ?? string.Empty;
            string deviceId = string.IsNullOrWhiteSpace(serialNumber) ? $"index:{index}" : serialNumber;
            string displayName = string.IsNullOrWhiteSpace(serialNumber)
                ? info.cameraName
                : $"{info.cameraName}（序列号：{serialNumber}）";
            result.Add(new(deviceId, displayName));
        }
        return result;
    }

    private void CaptureOneFrame(ref IntPtr buffer, ref int capacity)
    {
        IMVDefine.IMV_Frame frame = new();
        if (_camera.IMV_GetFrame(ref frame, 50) != IMVDefine.IMV_OK) return;

        int width = (int)frame.frameInfo.width;
        int height = (int)frame.frameInfo.height;
        int length = checked(width * height * 3);
        byte[] pixels;
        try
        {
            if (length > capacity)
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal(length);
                capacity = length;
            }
            IMVDefine.IMV_PixelConvertParam conversion = new()
            {
                nWidth = frame.frameInfo.width,
                nHeight = frame.frameInfo.height,
                ePixelFormat = frame.frameInfo.pixelFormat,
                pSrcData = frame.pData,
                nSrcDataLen = frame.frameInfo.size,
                nPaddingX = frame.frameInfo.paddingX,
                nPaddingY = frame.frameInfo.paddingY,
                eBayerDemosaic = IMVDefine.IMV_EBayerDemosaic.demosaicBilinear,
                eDstPixelFormat = IMVDefine.IMV_EPixelType.gvspPixelBGR8,
                pDstBuf = buffer,
                nDstBufSize = (uint)capacity
            };
            Check(_camera.IMV_PixelConvert(ref conversion), "转换图像");
            pixels = new byte[length];
            Marshal.Copy(buffer, pixels, 0, length);
        }
        finally
        {
            _camera.IMV_ReleaseFrame(ref frame);
        }

        BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, width * 3);
        image.Freeze();
        FrameArrived?.Invoke(this, new VisionCameraFrameEventArgs(image));
    }

    private void StopCaptureCore()
    {
        if (!_grabberStarted) return;
        _capturing = false;
        Check(_camera.IMV_StopGrabbing(), "结束采集");
        _grabberStarted = false;
    }

    private void Check(int result, string action)
    {
        if (result != IMVDefine.IMV_OK) throw new InvalidOperationException($"{action}失败，SDK 错误码：{result}。");
    }
}