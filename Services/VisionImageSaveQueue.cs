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

    public async ValueTask<Task<string>> QueueAsync(BitmapSource image, string directory, CancellationToken ct = default)
    {
        SaveRequest request = CreateRequest(image, directory);
        await _requests.Writer.WriteAsync(request, ct).ConfigureAwait(false);
        return request.Completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        _requests.Writer.TryComplete();
        await Task.Run(_writerThread.Join).ConfigureAwait(false);
    }

    private SaveRequest CreateRequest(BitmapSource image, string directory)
    {
        string filePath = Path.Combine(directory, $"相机_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Interlocked.Increment(ref _fileSequence):D6}.bmp");
        TaskCompletionSource<string> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return new SaveRequest(image, filePath, result);
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

    private sealed record SaveRequest(BitmapSource Image, string FilePath, TaskCompletionSource<string> Completion);
}