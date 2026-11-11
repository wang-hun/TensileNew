using System.IO;
using System.Threading.Channels;
using System.Windows.Media.Imaging;

namespace TensileNeW.Services;

public sealed class VisionImageSaveQueue : IAsyncDisposable
{
    private const int QueueCapacity = 2;
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
        _writerThread = new Thread(ImageWriterLoop)
        {
            IsBackground = true,
            Name = "Vision image writer"
        };
        _writerThread.Start();
    }

    public async ValueTask<Task<string>> QueueAsync(BitmapSource image, string directory, CancellationToken ct = default, string? latestPath = null, Action? latestSaved = null, Func<bool>? mayPublish = null, Action<Exception>? latestFailed = null, Action? latestPublishing = null)
    {
        SaveRequest request = CreateRequest(image, directory, latestPath, latestSaved, mayPublish, latestFailed, latestPublishing);
        await _requests.Writer.WriteAsync(request, ct).ConfigureAwait(false);
        return request.Completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        _requests.Writer.TryComplete();
        await Task.Run(_writerThread.Join).ConfigureAwait(false);
    }

    private SaveRequest CreateRequest(BitmapSource image, string directory, string? latestPath, Action? latestSaved, Func<bool>? mayPublish, Action<Exception>? latestFailed, Action? latestPublishing)
    {
        string filePath = Path.Combine(directory, $"相机_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Interlocked.Increment(ref _fileSequence):D6}.bmp");
        TaskCompletionSource<string> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return new SaveRequest(image, filePath, result, latestPath, latestSaved, mayPublish, latestFailed, latestPublishing);
    }

    private void ImageWriterLoop()
    {
        while (true)
        {
            while (_requests.Reader.TryRead(out SaveRequest? request))
            {
                try
                {
                    SaveImage(request.Image, request.FilePath);
                    if (request.LatestPath is not null && request.MayPublish?.Invoke() != false)
                    {
                        try
                        {
                            string latestDirectory = Path.GetDirectoryName(request.LatestPath) ?? throw new InvalidOperationException();
                            Directory.CreateDirectory(latestDirectory);
                            string temporaryPath = request.LatestPath + ".tmp";
                            File.Copy(request.FilePath, temporaryPath, true);
                            if (request.MayPublish?.Invoke() != false)
                            {
                                request.LatestPublishing?.Invoke();
                                File.Move(temporaryPath, request.LatestPath, true);
                                request.LatestSaved?.Invoke();
                            }
                            else File.Delete(temporaryPath);
                        }
                        catch (Exception exception) { request.LatestFailed?.Invoke(exception); }
                    }
                    request.Completion.TrySetResult(request.FilePath);
                }
                catch (Exception exception)
                {
                    request.Completion.TrySetException(exception);
                }
            }

            if (_requests.Reader.Completion.IsCompleted) return;
            if (!_requests.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult()) return;
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

    private sealed record SaveRequest(BitmapSource Image, string FilePath, TaskCompletionSource<string> Completion, string? LatestPath, Action? LatestSaved, Func<bool>? MayPublish, Action<Exception>? LatestFailed, Action? LatestPublishing);
}