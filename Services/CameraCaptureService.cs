using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using NLog;
using WinRT;

namespace TensileNeW.Services;

/// <summary>
/// Gives direct access to the bytes behind a WinRT memory buffer so camera frames can be
/// read without allocating an intermediate WinRT buffer per frame.
/// <para>
/// IMemoryBufferByteAccess must be reached by an explicit QueryInterface on the object's
/// native pointer. A [ComImport] interface cast does not work here: CsWinRT projects WinRT
/// objects through its own marshalling layer rather than classic COM interop, so casting
/// the projected IMemoryBufferReference throws InvalidCastException from WinRT.IInspectable.
/// </para>
/// </summary>
internal static unsafe class MemoryBufferByteAccess
{
    private static readonly Guid ByteAccessIid = new("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D");

    public static void GetBuffer(IMemoryBufferReference reference, out byte* buffer, out uint capacity)
    {
        // Marshal.GetIUnknownForObject must NOT be used here: it hands back a CsWinRT
        // wrapper that does not expose the native interface, and QueryInterface for
        // IMemoryBufferByteAccess on it fails with E_NOINTERFACE. IWinRTObject exposes
        // the real native pointer, which is borrowed (not AddRef'd) for this call.
        nint nativePointer = ((IWinRTObject)reference).NativeObject.ThisPtr;
        if (nativePointer == 0)
        {
            throw new InvalidOperationException("WinRT memory buffer reference has no native pointer.");
        }

        nint byteAccessPointer = 0;
        try
        {
            Guid iid = ByteAccessIid;
            int hr = Marshal.QueryInterface(nativePointer, ref iid, out byteAccessPointer);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            // IMemoryBufferByteAccess layout: IUnknown (0..2) then GetBuffer at slot 3.
            void** vtable = *(void***)byteAccessPointer;
            var getBuffer = (delegate* unmanaged[Stdcall]<nint, byte**, uint*, int>)vtable[3];

            byte* localBuffer;
            uint localCapacity;
            hr = getBuffer(byteAccessPointer, &localBuffer, &localCapacity);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            buffer = localBuffer;
            capacity = localCapacity;
        }
        finally
        {
            if (byteAccessPointer != 0)
            {
                Marshal.Release(byteAccessPointer);
            }
        }
    }
}

public sealed record CameraDeviceDescriptor(string Id, string Name);

public sealed record CameraStartupResult(
    IReadOnlyList<CameraDeviceDescriptor> Devices,
    CameraCaptureService? CaptureService,
    CameraDeviceDescriptor? SelectedDevice,
    string? FailureMessage)
{
    public bool Connected => CaptureService is not null && SelectedDevice is not null && string.IsNullOrWhiteSpace(FailureMessage);
}

public sealed record CameraFrameSnapshot(
    int Width,
    int Height,
    int Stride,
    DateTimeOffset Timestamp,
    byte[] BgraPixels);

public sealed class CameraFrameArrivedEventArgs : EventArgs
{
    public CameraFrameArrivedEventArgs(CameraFrameSnapshot frame, WriteableBitmap? bitmap)
    {
        Frame = frame;
        Bitmap = bitmap;
    }

    public CameraFrameSnapshot Frame { get; }

    public WriteableBitmap? Bitmap { get; }
}

public sealed class CameraCaptureService : IAsyncDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public const int MinimumWindowsMajorVersion = 10;
    public const int MinimumWindowsBuildNumber = 14393;

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly object _renderLock = new();

    // Two rotating pixel buffers. The render gate guarantees at most one frame is
    // awaiting the UI thread, so alternating buffers means the capture thread never
    // writes into the buffer the UI thread is currently reading.
    private readonly byte[]?[] _pixelBuffers = new byte[2][];
    private int _pixelBufferLength;
    private int _pixelBufferIndex;

    private readonly object _demandLock = new();
    private readonly Dictionary<string, (int Width, int Height)> _displayDemands = new();

    private MediaCapture? _mediaCapture;
    private MediaFrameSource? _frameSource;
    private MediaFrameFormat? _appliedFormat;
    private Task _formatSwitchTask = Task.CompletedTask;
    private MediaFrameReader? _frameReader;
    private Dispatcher? _bitmapDispatcher;
    private WriteableBitmap? _bitmap;
    private CameraFrameSnapshot? _latestRenderFrame;
    private bool _isRunning;
    private bool _renderPending;

    public event EventHandler<CameraFrameArrivedEventArgs>? FrameArrived;

    public event EventHandler<Exception>? CaptureFailed;

    public bool IsRunning => _isRunning;

    public string? CurrentDeviceId { get; private set; }

    public WriteableBitmap? CurrentBitmap => _bitmap;

    public static bool IsSupported =>
        OperatingSystem.IsWindowsVersionAtLeast(MinimumWindowsMajorVersion, 0, MinimumWindowsBuildNumber);

    public static async Task<IReadOnlyList<CameraDeviceDescriptor>> FindVideoCaptureDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnsupportedOperatingSystem();

        DeviceInformationCollection devices = await DeviceInformation
            .FindAllAsync(DeviceClass.VideoCapture)
            .AsTask(cancellationToken);

        return devices
            .Select(device => new CameraDeviceDescriptor(device.Id, device.Name))
            .ToList();
    }

    public async Task StartAsync(
        string videoDeviceId,
        Dispatcher? bitmapDispatcher = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoDeviceId))
        {
            throw new ArgumentException("Camera device id cannot be empty.", nameof(videoDeviceId));
        }

        ThrowIfUnsupportedOperatingSystem();

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();

            MediaCapture mediaCapture = new();
            MediaCaptureInitializationSettings settings = new()
            {
                VideoDeviceId = videoDeviceId,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            };

            await mediaCapture.InitializeAsync(settings).AsTask(cancellationToken);

            MediaFrameSource frameSource = SelectColorFrameSource(mediaCapture);

            if (Logger.IsDebugEnabled)
            {
                LogSupportedFormats(frameSource);
            }

            // Match the device format to current display demand before the first frame,
            // so an 8K sensor never streams 8K just to fill a small preview.
            (int demandWidth, int demandHeight) = GetCurrentDisplayDemand();
            MediaFrameFormat? initialFormat = SelectFormatForDemand(frameSource, demandWidth, demandHeight);
            MediaFrameFormat? appliedFormat = null;
            if (initialFormat is not null)
            {
                try
                {
                    await frameSource.SetFormatAsync(initialFormat).AsTask(cancellationToken);
                    appliedFormat = initialFormat;
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "摄像头初始帧格式设置失败，使用设备默认格式。");
                }
            }

            MediaFrameReader frameReader = await mediaCapture
                .CreateFrameReaderAsync(frameSource, MediaEncodingSubtypes.Bgra8)
                .AsTask(cancellationToken);

            frameReader.FrameArrived += OnFrameArrived;

            MediaFrameReaderStartStatus startStatus = await frameReader
                .StartAsync()
                .AsTask(cancellationToken);

            if (startStatus != MediaFrameReaderStartStatus.Success)
            {
                frameReader.FrameArrived -= OnFrameArrived;
                frameReader.Dispose();
                mediaCapture.Dispose();
                throw new InvalidOperationException($"Camera frame reader failed to start: {startStatus}.");
            }

            _mediaCapture = mediaCapture;
            _frameSource = frameSource;
            _appliedFormat = appliedFormat;
            _frameReader = frameReader;
            _bitmapDispatcher = bitmapDispatcher;
            _bitmap = null;
            _isRunning = true;
            CurrentDeviceId = videoDeviceId;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "摄像头初始化失败。");
            _isRunning = false;
            CurrentDeviceId = null;
            _bitmapDispatcher = null;
            _bitmap = null;
            _frameSource = null;
            _appliedFormat = null;
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public void SetBitmapDispatcher(Dispatcher? bitmapDispatcher)
    {
        _bitmapDispatcher = bitmapDispatcher;
        ResetRenderState();
    }

    public async Task StopAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stateLock.Dispose();
    }

    /// <summary>
    /// Registers how many pixels a named consumer currently needs. The service streams
    /// the smallest device format that still covers the largest live consumer, so a
    /// small sidebar preview costs little while a maximised window gets full detail.
    /// Pass a zero or negative size to withdraw the consumer.
    /// </summary>
    public void ReportDisplayDemand(string consumerKey, int pixelWidth, int pixelHeight)
    {
        if (string.IsNullOrWhiteSpace(consumerKey))
        {
            return;
        }

        lock (_demandLock)
        {
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                if (!_displayDemands.Remove(consumerKey))
                {
                    return;
                }
            }
            else
            {
                if (_displayDemands.TryGetValue(consumerKey, out (int Width, int Height) existing) &&
                    existing.Width == pixelWidth &&
                    existing.Height == pixelHeight)
                {
                    return;
                }

                _displayDemands[consumerKey] = (pixelWidth, pixelHeight);
            }
        }

        QueueFormatReevaluation();
    }

    private (int Width, int Height) GetCurrentDisplayDemand()
    {
        lock (_demandLock)
        {
            int width = 0;
            int height = 0;
            foreach ((int demandWidth, int demandHeight) in _displayDemands.Values)
            {
                width = Math.Max(width, demandWidth);
                height = Math.Max(height, demandHeight);
            }

            return (width, height);
        }
    }

    /// <summary>
    /// Serialises format switches onto a background chain so UI-thread layout and
    /// window events never wait on SetFormatAsync.
    /// </summary>
    private void QueueFormatReevaluation()
    {
        lock (_demandLock)
        {
            _formatSwitchTask = _formatSwitchTask.ContinueWith(
                _ => ApplyBestFormatForDemandAsync(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    private async Task ApplyBestFormatForDemandAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            MediaFrameSource? frameSource = _frameSource;
            if (!_isRunning || frameSource is null)
            {
                return;
            }

            (int demandWidth, int demandHeight) = GetCurrentDisplayDemand();
            MediaFrameFormat? target = SelectFormatForDemand(frameSource, demandWidth, demandHeight);
            if (target is null || ReferenceEquals(target, _appliedFormat))
            {
                return;
            }

            await frameSource.SetFormatAsync(target).AsTask().ConfigureAwait(false);
            _appliedFormat = target;

            // The frame geometry changed, so cached buffers and the bitmap are stale.
            ResetRenderState();

            Logger.Info(
                "摄像头帧格式已切换为 {0}x{1} @ {2:F1}fps（显示需求 {3}x{4}）。",
                target.VideoFormat.Width,
                target.VideoFormat.Height,
                GetFrameRate(target),
                demandWidth,
                demandHeight);
        }
        catch (Exception ex)
        {
            // A device that refuses the switch keeps streaming its previous format.
            Logger.Warn(ex, "摄像头帧格式切换失败，保持当前格式。");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Picks the cheapest format that still covers the requested size on both axes.
    /// The device's own format list is the only source of candidates, so an 8K camera
    /// serving an 8K consumer streams 8K; nothing is capped to a hardcoded ceiling.
    /// </summary>
    private static MediaFrameFormat? SelectFormatForDemand(
        MediaFrameSource frameSource,
        int demandWidth,
        int demandHeight)
    {
        List<MediaFrameFormat> candidates = frameSource.SupportedFormats
            .Where(format => IsColorFormat(format) && format.VideoFormat.Width > 0 && format.VideoFormat.Height > 0)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        if (demandWidth <= 0 || demandHeight <= 0)
        {
            // No live consumer: stream the smallest format available.
            return candidates
                .OrderBy(format => (long)format.VideoFormat.Width * format.VideoFormat.Height)
                .ThenByDescending(GetFrameRate)
                .First();
        }

        List<MediaFrameFormat> covering = candidates
            .Where(format => format.VideoFormat.Width >= demandWidth && format.VideoFormat.Height >= demandHeight)
            .ToList();

        if (covering.Count > 0)
        {
            return covering
                .OrderBy(format => (long)format.VideoFormat.Width * format.VideoFormat.Height)
                .ThenByDescending(GetFrameRate)
                .First();
        }

        // Demand exceeds everything the device offers: use its highest resolution.
        return candidates
            .OrderByDescending(format => (long)format.VideoFormat.Width * format.VideoFormat.Height)
            .ThenByDescending(GetFrameRate)
            .First();
    }

    private static bool IsColorFormat(MediaFrameFormat format)
    {
        return !string.Equals(format.MajorType, "Audio", StringComparison.OrdinalIgnoreCase);
    }

    private static double GetFrameRate(MediaFrameFormat format)
    {
        MediaRatio? frameRate = format.FrameRate;
        if (frameRate is null || frameRate.Denominator == 0)
        {
            return 0;
        }

        return (double)frameRate.Numerator / frameRate.Denominator;
    }

    private static MediaFrameSource SelectColorFrameSource(MediaCapture mediaCapture)
    {
        MediaFrameSource? source = mediaCapture.FrameSources.Values
            .Where(item => item.Info.SourceKind == MediaFrameSourceKind.Color)
            .OrderBy(item => item.Info.MediaStreamType == MediaStreamType.VideoPreview ? 0 : 1)
            .FirstOrDefault();

        return source ?? throw new InvalidOperationException("No color video frame source was found.");
    }

    private static void ThrowIfUnsupportedOperatingSystem()
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException(
                $"WinRT camera capture requires Windows {MinimumWindowsMajorVersion} build {MinimumWindowsBuildNumber} or later.");
        }
    }

    private async Task StopCoreAsync()
    {
        _isRunning = false;
        CurrentDeviceId = null;

        MediaFrameReader? frameReader = _frameReader;
        _frameReader = null;
        if (frameReader is not null)
        {
            frameReader.FrameArrived -= OnFrameArrived;
            try
            {
                await frameReader.StopAsync().AsTask();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "停止摄像头帧读取器失败。");
                // The reader may already be stopped when the camera is removed.
            }

            frameReader.Dispose();
        }

        _mediaCapture?.Dispose();
        _mediaCapture = null;
        _frameSource = null;
        _appliedFormat = null;
        _bitmapDispatcher = null;
        ResetRenderState();
    }

    private static void LogSupportedFormats(MediaFrameSource frameSource)
    {
        foreach (MediaFrameFormat format in frameSource.SupportedFormats)
        {
            Logger.Debug(
                "摄像头支持格式：{0} {1}x{2} @ {3:F1}fps",
                format.Subtype,
                format.VideoFormat.Width,
                format.VideoFormat.Height,
                GetFrameRate(format));
        }
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (!_isRunning)
        {
            return;
        }

        try
        {
            // Drop before doing any pixel work. The frame must still be acquired and
            // released so the reader can recycle it, but converting and copying a frame
            // the UI thread will never show is pure waste - at 8K that is ~126 MiB of
            // memcpy and large-object allocation per discarded frame.
            if (ShouldDropFrameBeforeDecode())
            {
                sender.TryAcquireLatestFrame()?.Dispose();
                return;
            }

            using MediaFrameReference? frameReference = sender.TryAcquireLatestFrame();
            if (frameReference?.VideoMediaFrame?.SoftwareBitmap is not SoftwareBitmap sourceBitmap)
            {
                return;
            }

            // The frame reader is created with MediaEncodingSubtypes.Bgra8, so the common
            // path needs no conversion at all. Alpha mode is deliberately not normalised:
            // camera frames carry no transparency, so Ignore and Premultiplied hold
            // identical bytes and the WPF side reads them as Bgr32.
            if (sourceBitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8)
            {
                PublishFrame(CopyFrame(sourceBitmap, frameReference.SystemRelativeTime));
                return;
            }

            using SoftwareBitmap convertedBitmap = SoftwareBitmap.Convert(
                sourceBitmap,
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore);
            PublishFrame(CopyFrame(convertedBitmap, frameReference.SystemRelativeTime));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "摄像头帧处理失败。");
            CaptureFailed?.Invoke(this, ex);
        }
    }

    private bool ShouldDropFrameBeforeDecode()
    {
        if (_bitmapDispatcher is null)
        {
            return false;
        }

        lock (_renderLock)
        {
            return _renderPending;
        }
    }

    private unsafe CameraFrameSnapshot CopyFrame(SoftwareBitmap bitmap, TimeSpan? systemRelativeTime)
    {
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;

        using BitmapBuffer bitmapBuffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using IMemoryBufferReference bufferReference = bitmapBuffer.CreateReference();

        BitmapPlaneDescription description = bitmapBuffer.GetPlaneDescription(0);
        // Never assume width * 4: high-resolution frames can carry row padding.
        int stride = description.Stride;
        int length = stride * height;

        byte[] pixels = RentPixelBuffer(length);

        MemoryBufferByteAccess.GetBuffer(bufferReference, out byte* sourcePointer, out uint capacity);
        int available = (int)Math.Min(capacity - (uint)description.StartIndex, (uint)length);
        fixed (byte* destinationPointer = pixels)
        {
            System.Buffer.MemoryCopy(
                sourcePointer + description.StartIndex,
                destinationPointer,
                length,
                available);
        }

        DateTimeOffset timestamp = systemRelativeTime.HasValue
            ? DateTimeOffset.UtcNow - systemRelativeTime.Value
            : DateTimeOffset.UtcNow;

        return new CameraFrameSnapshot(width, height, stride, timestamp, pixels);
    }

    /// <summary>
    /// Hands out one of two persistent pixel buffers instead of allocating per frame.
    /// An 8K BGRA frame is ~126 MiB, so a fresh array per frame lands on the large
    /// object heap and drives gen2 collections that suspend the PLC acquisition thread.
    /// </summary>
    private byte[] RentPixelBuffer(int length)
    {
        lock (_renderLock)
        {
            if (_pixelBufferLength != length)
            {
                _pixelBuffers[0] = null;
                _pixelBuffers[1] = null;
                _pixelBufferLength = length;
            }

            _pixelBufferIndex ^= 1;
            return _pixelBuffers[_pixelBufferIndex] ??= new byte[length];
        }
    }

    private void PublishFrame(CameraFrameSnapshot snapshot)
    {
        Dispatcher? dispatcher = _bitmapDispatcher;
        if (dispatcher is null)
        {
            FrameArrived?.Invoke(this, new CameraFrameArrivedEventArgs(snapshot, null));
            return;
        }

        lock (_renderLock)
        {
            _latestRenderFrame = snapshot;
            if (_renderPending)
            {
                return;
            }

            _renderPending = true;
        }

        dispatcher.BeginInvoke(() =>
        {
            CameraFrameSnapshot? latestSnapshot;
            lock (_renderLock)
            {
                latestSnapshot = _latestRenderFrame;
                _latestRenderFrame = null;
                _renderPending = false;
            }

            if (!_isRunning || latestSnapshot is null)
            {
                return;
            }

            WriteableBitmap bitmap = GetOrCreateBitmap(latestSnapshot);
            WriteSnapshotToBitmap(bitmap, latestSnapshot);

            FrameArrived?.Invoke(this, new CameraFrameArrivedEventArgs(latestSnapshot, bitmap));
        }, DispatcherPriority.Render);
    }

    private void ResetRenderState()
    {
        lock (_renderLock)
        {
            _latestRenderFrame = null;
            _renderPending = false;
            _bitmap = null;
            _pixelBuffers[0] = null;
            _pixelBuffers[1] = null;
            _pixelBufferLength = 0;
            _pixelBufferIndex = 0;
        }
    }

    private WriteableBitmap GetOrCreateBitmap(CameraFrameSnapshot snapshot)
    {
        if (_bitmap is null ||
            _bitmap.PixelWidth != snapshot.Width ||
            _bitmap.PixelHeight != snapshot.Height)
        {
            // Bgr32 rather than Pbgra32: video frames have no alpha channel, so the
            // captured bytes are already valid here and no premultiply pass is needed.
            _bitmap = new WriteableBitmap(
                snapshot.Width,
                snapshot.Height,
                96,
                96,
                PixelFormats.Bgr32,
                null);
        }

        return _bitmap;
    }

    /// <summary>
    /// Copies a snapshot straight into the bitmap back buffer. This replaces
    /// WritePixels, which would stage the pixels through another full-frame copy.
    /// </summary>
    private static unsafe void WriteSnapshotToBitmap(WriteableBitmap bitmap, CameraFrameSnapshot snapshot)
    {
        bitmap.Lock();
        try
        {
            int destinationStride = bitmap.BackBufferStride;
            int rowBytes = Math.Min(destinationStride, snapshot.Stride);
            byte* destination = (byte*)bitmap.BackBuffer;

            fixed (byte* source = snapshot.BgraPixels)
            {
                if (destinationStride == snapshot.Stride)
                {
                    System.Buffer.MemoryCopy(
                        source,
                        destination,
                        (long)destinationStride * snapshot.Height,
                        (long)snapshot.Stride * snapshot.Height);
                }
                else
                {
                    for (int row = 0; row < snapshot.Height; row++)
                    {
                        System.Buffer.MemoryCopy(
                            source + ((long)row * snapshot.Stride),
                            destination + ((long)row * destinationStride),
                            destinationStride,
                            rowBytes);
                    }
                }
            }

            bitmap.AddDirtyRect(new Int32Rect(0, 0, snapshot.Width, snapshot.Height));
        }
        finally
        {
            bitmap.Unlock();
        }
    }
}
