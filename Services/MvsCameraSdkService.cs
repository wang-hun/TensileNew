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
    private readonly object _gate = new();
    private readonly MyCamera _camera = new();
    private bool _handle;
    private bool _grabberStarted;
    private string? _connectedDeviceId;
    private volatile bool _capturing;
    private Thread? _thread;
    public event EventHandler<VisionCameraFrameEventArgs>? FrameArrived;
    public event EventHandler<VisionCameraErrorEventArgs>? CaptureFailed;
    public bool IsConnected { get; private set; }
    public bool IsCapturing => _capturing;

    public Task<IReadOnlyList<VisionCameraDevice>> EnumerateDevicesAsync(CancellationToken ct = default) => Task.Run<IReadOnlyList<VisionCameraDevice>>(() =>
    {
        ct.ThrowIfCancellationRequested();
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
    }, ct);

    public async Task ConnectAsync(string deviceId, CancellationToken ct = default)
    {
        if (_capturing && !string.Equals(_connectedDeviceId, deviceId, StringComparison.Ordinal))
        {
            await StopCaptureAsync(ct).ConfigureAwait(false);
        }
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (IsConnected && string.Equals(_connectedDeviceId, deviceId, StringComparison.Ordinal)) return;
                if (IsConnected)
                {
                    Check(_camera.IMV_Close(), "断开相机");
                    IsConnected = false;
                    _connectedDeviceId = null;
                }
                if (_handle)
                {
                    Check(_camera.IMV_DestroyHandle(), "释放相机句柄");
                    _handle = false;
                }
            IMVDefine.IMV_DeviceList list = new();
            Check(MyCamera.IMV_EnumDevices(ref list, (uint)IMVDefine.IMV_EInterfaceType.interfaceTypeAll), "枚举相机");
            int size = Marshal.SizeOf(typeof(IMVDefine.IMV_DeviceInfo));
            int selectedIndex = -1;
            for (int index = 0; index < list.nDevNum; index++)
            {
                IMVDefine.IMV_DeviceInfo info = Marshal.PtrToStructure<IMVDefine.IMV_DeviceInfo>(list.pDevInfo + index * size);
                string serialNumber = info.serialNumber?.Trim() ?? string.Empty;
                string currentId = string.IsNullOrWhiteSpace(serialNumber) ? $"index:{index}" : serialNumber;
                if (string.Equals(currentId, deviceId, StringComparison.Ordinal))
                {
                    selectedIndex = index;
                    break;
                }
            }
            if (selectedIndex < 0) throw new InvalidOperationException("未找到所选相机，请刷新设备列表。");
            Check(_camera.IMV_CreateHandle(IMVDefine.IMV_ECreateHandleMode.modeByIndex, selectedIndex), "创建相机句柄");
            _handle = true;
            try { Check(_camera.IMV_Open(), "连接相机"); IsConnected = true; _connectedDeviceId = deviceId; }
            catch { _camera.IMV_DestroyHandle(); _handle = false; throw; }
            }
        }, ct).ConfigureAwait(false);
    }
    public Task StartCaptureAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!IsConnected) throw new InvalidOperationException("请先连接相机。");
            if (_capturing) return;
            Check(_camera.IMV_StartGrabbing(), "开始采集");
            _grabberStarted = true;
            _capturing = true;
            _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "MVS camera capture" };
            _thread.Start();
        }
    }, ct);

    public async Task StopCaptureAsync(CancellationToken ct = default)
    {
        Thread? thread;
        lock (_gate)
        {
            if (!_grabberStarted) return;
            _capturing = false;
            thread = _thread;
            Check(_camera.IMV_StopGrabbing(), "结束采集");
            _grabberStarted = false;
        }
        if (thread is not null) await Task.Run(() => thread.Join(TimeSpan.FromSeconds(3)), ct).ConfigureAwait(false);
        _thread = null;
    }

    public Task<VisionCameraParameters> GetParametersAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!IsConnected) throw new InvalidOperationException("相机尚未连接。");
            double exposure = 0, gain = 0;
            Check(_camera.IMV_GetDoubleFeatureValue("ExposureTime", ref exposure), "读取曝光");
            Check(_camera.IMV_GetDoubleFeatureValue("GainRaw", ref gain), "读取增益");
            return new VisionCameraParameters(exposure, gain);
        }
    }, ct);

    public Task SetParametersAsync(double exposure, double gain, CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!IsConnected) throw new InvalidOperationException("相机尚未连接。");
            Check(_camera.IMV_SetDoubleFeatureValue("ExposureTime", exposure), "设置曝光");
            Check(_camera.IMV_SetDoubleFeatureValue("GainRaw", gain), "设置增益");
        }
    }, ct);

    public async ValueTask DisposeAsync()
    {
        await StopCaptureAsync().ConfigureAwait(false);
        lock (_gate)
        {
            if (IsConnected) _camera.IMV_Close();
            IsConnected = false;
            _connectedDeviceId = null;
            if (_handle) _camera.IMV_DestroyHandle();
            _handle = false;
        }
    }

    private void CaptureLoop()
    {
        IntPtr buffer = IntPtr.Zero;
        int capacity = 0;
        IMVDefine.IMV_Frame frame = new();
        try
        {
            while (_capturing)
            {
                lock (_gate)
                {
                    if (!_capturing || _camera.IMV_GetFrame(ref frame, 500) != IMVDefine.IMV_OK) continue;
                    int width = (int)frame.frameInfo.width;
                    int height = (int)frame.frameInfo.height;
                    int length = checked(width * height * 3);
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
                    try
                    {
                        Check(_camera.IMV_PixelConvert(ref conversion), "转换图像");
                        byte[] pixels = new byte[length];
                        Marshal.Copy(buffer, pixels, 0, length);
                        BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, width * 3);
                        image.Freeze();
                        FrameArrived?.Invoke(this, new VisionCameraFrameEventArgs(image));
                    }
                    finally { _camera.IMV_ReleaseFrame(ref frame); }
                }
            }
        }
        catch (Exception exception)
        {
            _capturing = false;
            CaptureFailed?.Invoke(this, new VisionCameraErrorEventArgs(exception.Message));
        }
        finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
    }

    private void Check(int result, string action)
    {
        if (result != IMVDefine.IMV_OK) throw new InvalidOperationException($"{action}失败，SDK 错误码：{result}。");
    }
}
