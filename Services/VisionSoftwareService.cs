using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using NLog;
using TensileNeW.Models;

namespace TensileNeW.Services;

public sealed record VisionDefectBox(double CenterX, double CenterY, double Left, double Top, double Right, double Bottom);

public sealed class VisionSoftwareService : IAsyncDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly SettingModel _settings;
    private readonly Func<IReadOnlyList<VisionDefectBox>, Task> _onCrack;
    private readonly Action<string> _warning;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private TcpClient? _client;
    private StreamWriter? _writer;
    private bool _startSent;
    private bool _endPending;
    private volatile bool _active;
    private long _startReplies;
    private long _endReplies;
    private TaskCompletionSource<string>? _pendingResponse;
    private int _crackTriggered;
    private int _failureNotified;
    private readonly Func<bool> _hostAvailable;
    private Task? _connectionTask;

    public VisionSoftwareService(SettingModel settings, Func<IReadOnlyList<VisionDefectBox>, Task> onCrack, Action<string> warning, Func<bool> hostAvailable)
    {
        _settings = settings;
        _onCrack = onCrack;
        _warning = warning;
        _hostAvailable = hostAvailable;
    }

    public event Action<bool>? DetectionResultReceived;
    public event Action? DetectionStarted;
    public void StartConnection() => _connectionTask ??= Task.Run(ConnectionLoopAsync);

    public async Task StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_active) return;
            _active = true;
            Interlocked.Exchange(ref _crackTriggered, 0);
            if (_writer is null)
            {
                Report("视觉软件未连接，未发送开始检测。");
                return;
            }
            if (_endPending) await SendEndAsync(_writer);
            if (!_startSent) await SendStartAsync(_writer);
        }
        catch (Exception exception) { Report($"视觉软件发送 start 失败：{exception.Message}"); }
        finally { _gate.Release(); }
    }

    public async Task EndAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_active && !_startSent) return;
            _active = false;
            if (!_startSent) return;
            _endPending = true;
            if (_writer is null)
            {
                Report("视觉软件未连接，无法发送 end。");
                return;
            }
            await SendEndAsync(_writer);
        }
        catch (Exception exception) { Report($"视觉软件发送 end 失败：{exception.Message}"); }
        finally { _gate.Release(); }
    }

    private async Task SendStartAsync(StreamWriter writer)
    {
        await _requestGate.WaitAsync(_shutdown.Token);
        try
        {
            long sequence = Interlocked.Read(ref _startReplies);
            await writer.WriteLineAsync("start".AsMemory(), _shutdown.Token).WaitAsync(TimeSpan.FromSeconds(2));
            _startSent = true;
            DetectionStarted?.Invoke();
            Logger.Info("视觉软件：发送 start");
            _ = CheckControlReplyAsync("start", sequence);
        }
        finally { _requestGate.Release(); }
    }

    private async Task SendEndAsync(StreamWriter writer)
    {
        await _requestGate.WaitAsync(_shutdown.Token);
        try
        {
            long sequence = Interlocked.Read(ref _endReplies);
            await writer.WriteLineAsync("end".AsMemory(), _shutdown.Token).WaitAsync(TimeSpan.FromSeconds(2));
            _startSent = false;
            _endPending = false;
            Logger.Info("视觉软件：发送 end");
            _ = CheckControlReplyAsync("end", sequence);
        }
        finally { _requestGate.Release(); }
    }
    private async Task CheckControlReplyAsync(string command, long sequence)
    {
        try
        {
            await Task.Delay(Math.Max(1, _settings.VisionSoftwareReplyTimeoutMs), _shutdown.Token);
            long current = command == "start" ? Interlocked.Read(ref _startReplies) : Interlocked.Read(ref _endReplies);
            if (sequence == current) Report($"视觉软件未回复 {command}。");
        }
        catch (OperationCanceledException) { }
    }

    public void ImagePublishFailed(Exception exception) => Report($"视觉识别图片保存失败：{exception.Message}");

    public void Reconnect() => _client?.Dispose();

    public void ImagePublishing() { }

    public void ImagePublished() => _ = CheckImageAsync();

    public async Task<RingPixelRadii?> GetRadiiAsync()
    {
        string imagePath = GetConfiguredImagePath();
        string? response = await SendRequestAsync($"radius \"{imagePath}\"", replyTimeout: TimeSpan.FromSeconds(2));
        if (response is null) return null;
        string[] fields = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length == 3 && fields[0] == "radius" &&
            double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double innerRadius) && double.IsFinite(innerRadius) && innerRadius > 0d &&
            double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double outerRadius) && double.IsFinite(outerRadius) && outerRadius > 0d
            ? new RingPixelRadii(innerRadius, outerRadius)
            : null;
    }

    private async Task CheckImageAsync()
    {
        if (!_active) return;
        try
        {
            string imagePath = GetConfiguredImagePath();
            string? response = await SendRequestAsync($"check \"{imagePath}\"", requireActive: true);
            if (response is null && _active) Logger.Warn("视觉图片发布后未收到识别回复。");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception exception) { Report($"视觉检测请求失败：{exception.Message}"); }
    }
    private string GetConfiguredImagePath() => Path.GetFullPath(
        Path.Combine(_settings.VisionSoftwareImageDirectory, _settings.VisionSoftwareImageFileName),
        AppContext.BaseDirectory);

    private async Task<string?> SendRequestAsync(string command, bool requireActive = false, TimeSpan? replyTimeout = null)
    {
        await _requestGate.WaitAsync(_shutdown.Token);
        TaskCompletionSource<string>? response = null;
        try
        {
            if (requireActive && !_active) return null;
            StreamWriter? writer = _writer;
            if (writer is null)
            {
                Report("视觉软件未连接，未发送请求。");
                return null;
            }

            response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _pendingResponse, response);
            await writer.WriteLineAsync(command.AsMemory(), _shutdown.Token).WaitAsync(TimeSpan.FromSeconds(2));
            Logger.Info("视觉软件：发送 {0}", command);
            return await response.Task.WaitAsync(replyTimeout ?? TimeSpan.FromMilliseconds(Math.Max(1, _settings.VisionSoftwareReplyTimeoutMs)), _shutdown.Token);
        }
        catch (TimeoutException)
        {
            Report($"视觉软件未在 {(replyTimeout ?? TimeSpan.FromMilliseconds(Math.Max(1, _settings.VisionSoftwareReplyTimeoutMs))).TotalMilliseconds:F0}ms 内回复 {command}。");
            _client?.Dispose();
            return null;
        }
        finally
        {
            if (response is not null) Interlocked.CompareExchange(ref _pendingResponse, null, response);
            _requestGate.Release();
        }
    }

    private async Task ConnectionLoopAsync()
    {
        DateTime startupDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!_shutdown.IsCancellationRequested)
        {
            if (!NewVisionProcessingService.IsIncludedByPackage || !_hostAvailable())
            {
                await DelayAsync();
                continue;
            }
            TcpClient client = new();
            try
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await client.ConnectAsync(_settings.VisionSoftwareHost, _settings.VisionSoftwarePort, timeout.Token);
                NetworkStream stream = client.GetStream();
                using StreamReader reader = new(stream, Encoding.UTF8, false, 1024, true);
                StreamWriter writer = new(stream, new UTF8Encoding(false), 1024, true) { AutoFlush = true, NewLine = "\n" };
                await _gate.WaitAsync(_shutdown.Token);
                try
                {
                    _client = client;
                    _writer = writer;
                    if (_endPending) await SendEndAsync(writer);
                    if (_active && !_startSent) await SendStartAsync(writer);
                }
                finally { _gate.Release(); }
                Interlocked.Exchange(ref _failureNotified, 0);
                Logger.Info("视觉软件已连接：{0}:{1}", _settings.VisionSoftwareHost, _settings.VisionSoftwarePort);
                while (!_shutdown.IsCancellationRequested)
                {
                    string? message = await reader.ReadLineAsync(_shutdown.Token);
                    if (message is null) throw new IOException("连接已断开");
                    Logger.Info("视觉软件收到：{0}", message);
                    if (message == "start") Interlocked.Increment(ref _startReplies);
                    if (message == "end") Interlocked.Increment(ref _endReplies);
                    if (message == "skip" || message == "stop" || message.StartsWith("stop ", StringComparison.Ordinal) || message.StartsWith("error ", StringComparison.Ordinal) || message.StartsWith("radius ", StringComparison.Ordinal))
                        Volatile.Read(ref _pendingResponse)?.TrySetResult(message);
                    if (message == "skip" && _active) DetectionResultReceived?.Invoke(false);
                    if ((message == "stop" || message.StartsWith("stop ", StringComparison.Ordinal)) && _active && Interlocked.Exchange(ref _crackTriggered, 1) == 0)
                    {
                        _active = false;
                        DetectionResultReceived?.Invoke(true);
                        _ = Task.Run(() => HandleStopReplyAsync(message));
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (exception is SocketException && DateTime.UtcNow < startupDeadline)
                    Logger.Info("识别程序尚未开始监听：{0}", exception.Message);
                else
                {
                    string message = $"视觉软件连接异常：{exception.Message}";
                    if (Interlocked.Exchange(ref _failureNotified, 1) == 0) Report(message);
                    else Logger.Warn(message);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _pendingResponse, null)?.TrySetException(new IOException("视觉软件连接已断开。"));
                await _gate.WaitAsync();
                try
                {
                    if (ReferenceEquals(_client, client))
                    {
                        _writer = null;
                        _client = null;
                        _startSent = false;
                    }
                }
                finally { _gate.Release(); }
                client.Dispose();
            }
            if (DateTime.UtcNow < startupDeadline)
            {
                try { await Task.Delay(250, _shutdown.Token); }
                catch (OperationCanceledException) { }
            }
            else await DelayAsync();
        }
    }

    private async Task HandleStopReplyAsync(string message)
    {
        IReadOnlyList<VisionDefectBox> defects;
        try { defects = ParseStopReply(message); }
        catch (FormatException exception)
        {
            Report($"视觉软件 stop 回复坐标无效：{exception.Message}");
            defects = [];
        }

        try { await _onCrack(defects); }
        catch (Exception exception)
        {
            Logger.Error(exception, "视觉缺陷停机处理失败。");
            Report($"视觉缺陷停机处理失败：{exception.Message}");
        }
    }

    private static IReadOnlyList<VisionDefectBox> ParseStopReply(string message)
    {
        string[] fields = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 1) return [];
        if (fields.Length < 2 || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 0)
            throw new FormatException("缺陷数量无效。");
        if (count == 0) return [];
        if (fields.Length != count + 2) throw new FormatException("缺陷坐标数量与缺陷数不一致。");

        List<VisionDefectBox> defects = new(count);
        for (int index = 0; index < count; index++)
        {
            string[] coordinates = fields[index + 2].Split(',');
            if (coordinates.Length != 6) throw new FormatException("每个缺陷必须包含六个坐标值。");
            double[] values = coordinates.Select(value =>
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || !double.IsFinite(parsed))
                    throw new FormatException("坐标值不是有效有限数。");
                return parsed;
            }).ToArray();
            defects.Add(new VisionDefectBox(values[0], values[1], values[2], values[3], values[4], values[5]));
        }
        return defects;
    }

    private async Task DelayAsync()
    {
        try { await Task.Delay(TimeSpan.FromSeconds(5), _shutdown.Token); }
        catch (OperationCanceledException) { }
    }

    private void Report(string message) { Logger.Warn(message); _warning(message); }

    public async ValueTask DisposeAsync()
    {
        await EndAsync();
        _shutdown.Cancel();
        _client?.Dispose();
        if (_connectionTask is not null) await _connectionTask;
        _shutdown.Dispose();
        _requestGate.Dispose();
        _gate.Dispose();
    }
}
