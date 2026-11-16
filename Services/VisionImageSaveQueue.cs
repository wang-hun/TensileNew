using System.Diagnostics;
using System.IO;
using NLog;
using System.Threading.Channels;
using System.Windows.Media.Imaging;

namespace TensileNeW.Services;

public sealed class VisionImageSaveQueue : IAsyncDisposable
{
    private const int QueueCapacity = 2;
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly Channel<SaveRequest> _requests = Channel.CreateBounded<SaveRequest>(new BoundedChannelOptions(QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Thread _writerThread;
    private long _fileSequence;

    public VisionImageSaveQueue()
    {
        _writerThread = new Thread(ImageWriterLoop) { IsBackground = true, Name = "Vision image writer" };
        _writerThread.Start();
    }

    public async ValueTask<Task<string>> QueueAsync(BitmapSource image, string directory, CancellationToken ct = default, string? latestPath = null, Action<byte[]>? latestSaved = null, Func<bool>? mayPublish = null, Action<Exception>? latestFailed = null)
    {
        SaveRequest request = CreateRequest(image, directory, latestPath, latestSaved, mayPublish, latestFailed);
        await _requests.Writer.WriteAsync(request, ct).ConfigureAwait(false);
        return request.Completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        _requests.Writer.TryComplete();
        await Task.Run(_writerThread.Join).ConfigureAwait(false);
    }

    private SaveRequest CreateRequest(BitmapSource image, string directory, string? latestPath, Action<byte[]>? latestSaved, Func<bool>? mayPublish, Action<Exception>? latestFailed)
    {
        string filePath = Path.Combine(directory, $"相机_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Interlocked.Increment(ref _fileSequence):D6}.bmp");
        TaskCompletionSource<string> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return new SaveRequest(image, filePath, result, latestPath, latestSaved, mayPublish, latestFailed, Stopwatch.GetTimestamp());
    }

    private void ImageWriterLoop()
    {
        while (_requests.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            while (_requests.Reader.TryRead(out SaveRequest? request))
            {
                try
                {
                    Stopwatch total = Stopwatch.StartNew();
                    SaveImage(request.Image, request.FilePath);
                    double writeMilliseconds = total.Elapsed.TotalMilliseconds;
                    double copyMilliseconds = 0;
                    double readMilliseconds = 0;
                    int imageLength = 0;
                    if (request.LatestPath is not null && request.MayPublish?.Invoke() != false)
                    {
                        string? temporaryPath = null;
                        try
                        {
                            string latestDirectory = Path.GetDirectoryName(request.LatestPath) ?? throw new InvalidOperationException();
                            Directory.CreateDirectory(latestDirectory);
                            temporaryPath = request.LatestPath + ".tmp";
                            Stopwatch stage = Stopwatch.StartNew();
                            File.Copy(request.FilePath, temporaryPath, true);
                            if (request.MayPublish?.Invoke() != false)
                            {
                                File.Move(temporaryPath, request.LatestPath, true);
                                temporaryPath = null;
                                copyMilliseconds = stage.Elapsed.TotalMilliseconds;
                                if (request.LatestSaved is not null)
                                {
                                    stage.Restart();
                                    byte[] detectionImage = File.ReadAllBytes(request.LatestPath);
                                    readMilliseconds = stage.Elapsed.TotalMilliseconds;
                                    imageLength = detectionImage.Length;
                                    Stopwatch handoff = Stopwatch.StartNew();
                                    request.LatestSaved(detectionImage);
                                    Logger.Info("视觉检测图片交接：保存队列等待 {0:F1}ms，BMP写盘 {1:F1}ms，固定文件副本 {2:F1}ms，读副本 {3:F1}ms，入检测队列 {4:F1}ms，字节数 {5}。",
                                        Stopwatch.GetElapsedTime(request.QueuedAtTimestamp, Stopwatch.GetTimestamp()).TotalMilliseconds - total.Elapsed.TotalMilliseconds,
                                        writeMilliseconds, copyMilliseconds, readMilliseconds, handoff.Elapsed.TotalMilliseconds, imageLength);
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            request.LatestFailed?.Invoke(exception);
                        }
                        finally
                        {
                            if (temporaryPath is not null && File.Exists(temporaryPath)) File.Delete(temporaryPath);
                        }
                    }
                    request.Completion.TrySetResult(request.FilePath);
                }
                catch (Exception exception)
                {
                    request.Completion.TrySetException(exception);
                }
            }
        }
    }

    private static void SaveImage(BitmapSource image, string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("图片保存路径无效。");
        Directory.CreateDirectory(directory);
        BmpBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using FileStream stream = new(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private sealed record SaveRequest(BitmapSource Image, string FilePath, TaskCompletionSource<string> Completion, string? LatestPath, Action<byte[]>? LatestSaved, Func<bool>? MayPublish, Action<Exception>? LatestFailed, long QueuedAtTimestamp);
}