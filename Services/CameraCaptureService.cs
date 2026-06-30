using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace TensileNeW.Services;

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
    public const int MinimumWindowsMajorVersion = 10;
    public const int MinimumWindowsBuildNumber = 14393;

    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private MediaCapture? _mediaCapture;
    private MediaFrameReader? _frameReader;
    private Dispatcher? _bitmapDispatcher;
    private WriteableBitmap? _bitmap;
    private bool _isRunning;

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
            _frameReader = frameReader;
            _bitmapDispatcher = bitmapDispatcher;
            _bitmap = null;
            _isRunning = true;
            CurrentDeviceId = videoDeviceId;
        }
        catch
        {
            _isRunning = false;
            CurrentDeviceId = null;
            _bitmapDispatcher = null;
            _bitmap = null;
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
        _bitmap = null;
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
            catch
            {
                // The reader may already be stopped when the camera is removed.
            }

            frameReader.Dispose();
        }

        _mediaCapture?.Dispose();
        _mediaCapture = null;
        _bitmapDispatcher = null;
        _bitmap = null;
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (!_isRunning)
        {
            return;
        }

        try
        {
            using MediaFrameReference? frameReference = sender.TryAcquireLatestFrame();
            if (frameReference?.VideoMediaFrame?.SoftwareBitmap is not SoftwareBitmap sourceBitmap)
            {
                return;
            }

            using SoftwareBitmap convertedBitmap = ConvertToBgra8(sourceBitmap);
            CameraFrameSnapshot snapshot = CopyFrame(convertedBitmap, frameReference.SystemRelativeTime);
            PublishFrame(snapshot);
        }
        catch (Exception ex)
        {
            CaptureFailed?.Invoke(this, ex);
        }
    }

    private static SoftwareBitmap ConvertToBgra8(SoftwareBitmap sourceBitmap)
    {
        if (sourceBitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 &&
            sourceBitmap.BitmapAlphaMode == BitmapAlphaMode.Premultiplied)
        {
            return SoftwareBitmap.Copy(sourceBitmap);
        }

        return SoftwareBitmap.Convert(sourceBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    private static CameraFrameSnapshot CopyFrame(SoftwareBitmap bitmap, TimeSpan? systemRelativeTime)
    {
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        Windows.Storage.Streams.Buffer buffer = new((uint)pixels.Length);
        bitmap.CopyToBuffer(buffer);

        using DataReader reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(pixels);

        DateTimeOffset timestamp = systemRelativeTime.HasValue
            ? DateTimeOffset.UtcNow - systemRelativeTime.Value
            : DateTimeOffset.UtcNow;

        return new CameraFrameSnapshot(width, height, stride, timestamp, pixels);
    }

    private void PublishFrame(CameraFrameSnapshot snapshot)
    {
        Dispatcher? dispatcher = _bitmapDispatcher;
        if (dispatcher is null)
        {
            FrameArrived?.Invoke(this, new CameraFrameArrivedEventArgs(snapshot, null));
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            WriteableBitmap bitmap = GetOrCreateBitmap(snapshot);
            bitmap.WritePixels(
                new Int32Rect(0, 0, snapshot.Width, snapshot.Height),
                snapshot.BgraPixels,
                snapshot.Stride,
                0);

            FrameArrived?.Invoke(this, new CameraFrameArrivedEventArgs(snapshot, bitmap));
        }, DispatcherPriority.Render);
    }

    private WriteableBitmap GetOrCreateBitmap(CameraFrameSnapshot snapshot)
    {
        if (_bitmap is null ||
            _bitmap.PixelWidth != snapshot.Width ||
            _bitmap.PixelHeight != snapshot.Height)
        {
            _bitmap = new WriteableBitmap(
                snapshot.Width,
                snapshot.Height,
                96,
                96,
                PixelFormats.Pbgra32,
                null);
        }

        return _bitmap;
    }
}
