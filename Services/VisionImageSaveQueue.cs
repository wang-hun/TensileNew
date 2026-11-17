using System.Diagnostics;
using System.IO;
using NLog;
using TensileNeW.Models;
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

    public async ValueTask<Task<string>> QueueAsync(BitmapSource image, string directory, CancellationToken ct = default, string? latestPath = null, Action<string, Task<string>>? latestSaved = null, Func<bool>? mayPublish = null, Func<string, Task>? nativeSave = null)
    {
        SaveRequest request = CreateRequest(image, directory, latestPath, latestSaved, mayPublish, nativeSave);
        await _requests.Writer.WriteAsync(request, ct).ConfigureAwait(false);
        return request.Completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        _requests.Writer.TryComplete();
        await Task.Run(_writerThread.Join).ConfigureAwait(false);
    }

    private SaveRequest CreateRequest(BitmapSource image, string directory, string? latestPath, Action<string, Task<string>>? latestSaved, Func<bool>? mayPublish, Func<string, Task>? nativeSave)
    {
        string filePath = Path.Combine(directory, $"相机_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Interlocked.Increment(ref _fileSequence):D6}.bmp");
        TaskCompletionSource<string> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return new SaveRequest(image, filePath, result, latestPath, latestSaved, mayPublish, nativeSave, RAM.SettingModel?.VisionTimingEnabled != false ? Stopwatch.GetTimestamp() : 0);
    }

    private void ImageWriterLoop()
    {
        while (_requests.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            while (_requests.Reader.TryRead(out SaveRequest? request))
            {
                try
                {
                    bool measureTiming = RAM.SettingModel?.VisionTimingEnabled != false && request.QueuedAtTimestamp != 0;
                    double queueWaitMilliseconds = measureTiming ? Stopwatch.GetElapsedTime(request.QueuedAtTimestamp).TotalMilliseconds : 0;
                    Stopwatch? total = measureTiming ? Stopwatch.StartNew() : null;
                    bool publishLatest = request.LatestPath is not null && request.MayPublish?.Invoke() != false;
                    double fixedWriteMilliseconds = 0;
                    double continuousCopyMilliseconds = 0;
                    if (publishLatest)
                    {
                        Stopwatch? stage = measureTiming ? Stopwatch.StartNew() : null;
                        SaveImage(request.Image, request.LatestPath!, request.NativeSave, overwrite: true);
                        if (stage is not null) fixedWriteMilliseconds = stage.Elapsed.TotalMilliseconds;
                        Stopwatch? handoff = measureTiming ? Stopwatch.StartNew() : null;
                        request.LatestSaved?.Invoke(request.LatestPath!, request.Completion.Task);
                        if (handoff is not null)
                            Logger.Info("视觉检测图片交接：保存队列等待 {0:F1}ms，固定路径落盘 {1:F1}ms，路径入检测队列 {2:F1}ms。",
                                queueWaitMilliseconds, fixedWriteMilliseconds, handoff.Elapsed.TotalMilliseconds);

                        stage?.Restart();
                        File.Copy(request.LatestPath!, request.FilePath, true);
                        if (stage is not null) continuousCopyMilliseconds = stage.Elapsed.TotalMilliseconds;
                    }
                    else
                    {
                        Stopwatch? stage = measureTiming ? Stopwatch.StartNew() : null;
                        SaveImage(request.Image, request.FilePath, request.NativeSave);
                        if (stage is not null) fixedWriteMilliseconds = stage.Elapsed.TotalMilliseconds;
                    }

                    if (total is not null)
                        Logger.Info("视觉图片保存完成：总耗时 {0:F1}ms，固定路径写盘 {1:F1}ms，连续路径副本 {2:F1}ms，文件 {3}。",
                            total.Elapsed.TotalMilliseconds, fixedWriteMilliseconds, continuousCopyMilliseconds, request.FilePath);
                    request.Completion.TrySetResult(request.FilePath);
                }
                catch (Exception exception)
                {
                    request.Completion.TrySetException(exception);
                }
            }
        }
    }

    private static void SaveImage(BitmapSource image, string filePath, Func<string, Task>? nativeSave, bool overwrite = false)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("图片保存路径无效。");
        Directory.CreateDirectory(directory);
        string targetPath = overwrite ? filePath + ".tmp" : filePath;
        try
        {
            if (nativeSave is not null)
                nativeSave(targetPath).GetAwaiter().GetResult();
            else
            {
                BmpBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using FileStream stream = new(targetPath, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);
                encoder.Save(stream);
            }
            if (overwrite) File.Move(targetPath, filePath, true);
        }
        finally
        {
            if (overwrite && File.Exists(targetPath)) File.Delete(targetPath);
        }
    }
    private sealed record SaveRequest(BitmapSource Image, string FilePath, TaskCompletionSource<string> Completion, string? LatestPath, Action<string, Task<string>>? LatestSaved, Func<bool>? MayPublish, Func<string, Task>? NativeSave, long QueuedAtTimestamp);
}