using System.IO;
using System.Net.Sockets;
using System.Text;
using NLog;
using TensileNeW.Models;

namespace TensileNeW.Services;

public sealed class VisionSoftwareService : IAsyncDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly SettingModel _settings;
    private readonly Func<Task> _onCrack;
    private readonly Action<string> _warning;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private TcpClient? _client;
    private StreamWriter? _writer;
    private bool _startSent;
    private bool _endPending;
    private volatile bool _active;
    private long _responseSequence;
    private long _imageResponseBaseline;
    private long _startReplies;
    private long _endReplies;
    private int _crackTriggered;
    private int _failureNotified;
    private readonly Task _connectionTask;

    public VisionSoftwareService(SettingModel settings, Func<Task> onCrack, Action<string> warning)
    {
        _settings = settings;
        _onCrack = onCrack;
        _warning = warning;
        _connectionTask = Task.Run(ConnectionLoopAsync);
    }

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
        long sequence = Interlocked.Read(ref _startReplies);
        await writer.WriteLineAsync("start".AsMemory(), _shutdown.Token).WaitAsync(TimeSpan.FromSeconds(2));
        _startSent = true;
        Logger.Info("视觉软件：发送 start");
        _ = CheckControlReplyAsync("start", sequence);
    }

    private async Task SendEndAsync(StreamWriter writer)
    {
        long sequence = Interlocked.Read(ref _endReplies);
        await writer.WriteLineAsync("end".AsMemory(), _shutdown.Token).WaitAsync(TimeSpan.FromSeconds(2));
        _startSent = false;
        _endPending = false;
        Logger.Info("视觉软件：发送 end");
        _ = CheckControlReplyAsync("end", sequence);
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

    public void ImagePublishing() => Interlocked.Exchange(ref _imageResponseBaseline, Interlocked.Read(ref _responseSequence));

    public void ImagePublished()
    {
        long sequence = Interlocked.Read(ref _imageResponseBaseline);
        _ = CheckReplyAsync(sequence, Math.Max(1, _settings.VisionSoftwareReplyTimeoutMs));
    }

    private async Task CheckReplyAsync(long sequence, int timeout)
    {
        try
        {
            await Task.Delay(timeout, _shutdown.Token);
            if (sequence == Interlocked.Read(ref _responseSequence)) Logger.Warn("视觉图片发布后 {0}ms 内未收到识别回复。", timeout);
        }
        catch (OperationCanceledException) { }
    }

    private async Task ConnectionLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            if (!_settings.VisionSoftwareEnabled || !NewVisionProcessingService.IsIncludedByPackage)
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
                    if (_active && !_startSent)
                    {
                        await SendStartAsync(writer);
                    }
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
                    if (message == "continue" || message == "stop") Interlocked.Increment(ref _responseSequence);
                    if (message == "stop" && _active && Interlocked.Exchange(ref _crackTriggered, 1) == 0) _ = _onCrack();
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            catch (Exception exception)
            {
                string message = $"视觉软件连接异常：{exception.Message}";
                if (Interlocked.Exchange(ref _failureNotified, 1) == 0) Report(message);
                else Logger.Warn(message);
            }
            finally
            {
                await _gate.WaitAsync();
                try { if (ReferenceEquals(_client, client)) { _writer = null; _client = null; } }
                finally { _gate.Release(); }
                client.Dispose();
            }
            await DelayAsync();
        }
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
        await _connectionTask;
        _shutdown.Dispose();
        _gate.Dispose();
    }
}
