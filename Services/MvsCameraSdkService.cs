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
        Check(MyCamera.IMV_EnumDevices(ref list, (uint)IMVDefine.IMV_EInterfaceType.interfaceTypeAll), "鏋氫妇鐩告満");
        List<VisionCameraDevice> result = new();
        int size = Marshal.SizeOf(typeof(IMVDefine.IMV_DeviceInfo));
        for (int i = 0; i < list.nDevNum; i++)
        {
            IMVDefine.IMV_DeviceInfo info = Marshal.PtrToStructure<IMVDefine.IMV_DeviceInfo>(list.pDevInfo + i * size);
            result.Add(new(i.ToString(), $"{info.cameraName} [{info.serialNumber}]"));
        }
        return result;
    }, ct);
    public Task ConnectAsync(string id, CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (IsConnected) return;
            if (!int.TryParse(id, out int index)) throw new ArgumentException("Invalid camera selection.", nameof(id));
            Check(_camera.IMV_CreateHandle(IMVDefine.IMV_ECreateHandleMode.modeByIndex, index), "Create camera handle");
            _handle = true;
            try { Check(_camera.IMV_Open(), "Connect camera"); IsConnected = true; }
            catch { _camera.IMV_DestroyHandle(); _handle = false; throw; }
        }
    }, ct);

    public Task StartCaptureAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!IsConnected) throw new InvalidOperationException("Connect a camera first.");
            if (_capturing) return;
            Check(_camera.IMV_StartGrabbing(), "Start capture");
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
            Check(_camera.IMV_StopGrabbing(), "Stop capture");
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
            if (!IsConnected) throw new InvalidOperationException("Camera is not connected.");
            double exposure = 0, gain = 0;
            Check(_camera.IMV_GetDoubleFeatureValue("ExposureTime", ref exposure), "Read exposure");
            Check(_camera.IMV_GetDoubleFeatureValue("GainRaw", ref gain), "Read gain");
            return new VisionCameraParameters(exposure, gain);
        }
    }, ct);

    public Task SetParametersAsync(double exposure, double gain, CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!IsConnected) throw new InvalidOperationException("Camera is not connected.");
            Check(_camera.IMV_SetDoubleFeatureValue("ExposureTime", exposure), "Set exposure");
            Check(_camera.IMV_SetDoubleFeatureValue("GainRaw", gain), "Set gain");
        }
    }, ct);

    public async ValueTask DisposeAsync()
    {
        await StopCaptureAsync().ConfigureAwait(false);
        lock (_gate)
        {
            if (IsConnected) _camera.IMV_Close();
            IsConnected = false;
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
                        Check(_camera.IMV_PixelConvert(ref conversion), "Convert image");
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
        if (result != IMVDefine.IMV_OK) throw new InvalidOperationException($"{action} failed. SDK result: {result}.");
    }
}
