using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using RingInspect.Api;
using RingInspect.Api.Detection;
using TensileNeW.Models;

namespace TensileNeW.Services;

public sealed class VisionSoftwareService : IAsyncDisposable
{
    private const int DetectionQueueCapacity = 2;
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly SettingModel _settings;
    private readonly Func<Task> _onDefect;
    private readonly Action<string> _warning;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _apiRequestGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HttpClient _httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly Channel<DetectionWork> _detectionQueue = Channel.CreateBounded<DetectionWork>(new BoundedChannelOptions(DetectionQueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Task _detectionWorker;
    private readonly object _sessionSync = new();
    private readonly List<CancellationTokenSource> _sessionCancellations = [];
    private WebApplication? _apiHost;
    private Uri? _detectEndpoint;
    private volatile bool _active;
    private CancellationTokenSource? _activeSessionCancellation;
    private int _sessionGeneration;
    private int _defectHandled;
    private int _failureNotified;

    public VisionSoftwareService(SettingModel settings, Func<Task> onDefect, Action<string> warning)
    {
        _settings = settings;
        _onDefect = onDefect;
        _warning = warning;
        _detectionWorker = Task.Run(ProcessDetectionQueueAsync);
    }

    public event Action? DetectionStarted;
    public event Action<int>? DetectionCompleted;
    public event Action<string>? DetectionFailed;

    public async Task<bool> StartAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_active) return true;
            try
            {
                await EnsureApiStartedAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Report($"RingInspect 检测引擎启动失败：{exception.Message}");
                return false;
            }

            lock (_sessionSync)
            {
                _activeSessionCancellation?.Cancel();
                CancellationTokenSource sessionCancellation = new();
                _sessionCancellations.Add(sessionCancellation);
                _activeSessionCancellation = sessionCancellation;
                Interlocked.Exchange(ref _defectHandled, 0);
                Interlocked.Exchange(ref _failureNotified, 0);
                Interlocked.Increment(ref _sessionGeneration);
                _active = true;
            }
            Logger.Info("RingInspect 严格检测已启动。");
            return true;
        }
        finally { _lifecycleGate.Release(); }
    }

    public Task EndAsync()
    {
        lock (_sessionSync)
        {
            _active = false;
            Interlocked.Increment(ref _sessionGeneration);
            _activeSessionCancellation?.Cancel();
            _activeSessionCancellation = null;
        }
        Logger.Info("RingInspect 严格检测已停止。");
        return Task.CompletedTask;
    }

    public void ImagePublishFailed(Exception exception)
    {
        if (!IsCurrentSession(Volatile.Read(ref _sessionGeneration))) return;
        DetectionFailed?.Invoke(exception.Message);
        ReportFailure($"视觉识别图片保存失败：{exception.Message}");
    }

    public void ImagePublished(byte[] imageBytes)
    {
        if (imageBytes.Length == 0) return;

        DetectionWork work;
        lock (_sessionSync)
        {
            if (!_active || _detectEndpoint is null || _activeSessionCancellation is null) return;
            work = new DetectionWork(imageBytes, _settings.VisionSoftwareImageFileName,
                Volatile.Read(ref _sessionGeneration), Stopwatch.GetTimestamp(), _activeSessionCancellation.Token);
        }

        if (!_detectionQueue.Writer.TryWrite(work))
        {
            const string message = "RingInspect 检测队列已满，图片已保存但本帧未提交检测。";
            Logger.Warn(message);
            DetectionFailed?.Invoke(message);
        }
    }

    private async Task ProcessDetectionQueueAsync()
    {
        try
        {
            await foreach (DetectionWork work in _detectionQueue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                if (!IsCurrentSession(work.SessionGeneration)) continue;
                await DetectImageAsync(work).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private async Task DetectImageAsync(DetectionWork work)
    {
        if (_detectEndpoint is null || !IsCurrentSession(work.SessionGeneration)) return;

        Stopwatch total = Stopwatch.StartNew();
        Stopwatch stageTimer = new();
        CancellationTokenSource? timeout = null;
        string stage = "Base64编码";
        double base64Milliseconds = 0;
        double httpMilliseconds = 0;
        try
        {
            DetectionStarted?.Invoke();
            stageTimer.Start();
            string imageBase64 = Convert.ToBase64String(work.ImageBytes);
            base64Milliseconds = stageTimer.Elapsed.TotalMilliseconds;

            stage = "HTTP请求及响应";
            int replyTimeoutMilliseconds = Math.Max(100, _settings.VisionSoftwareReplyTimeoutMs);
            timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, work.SessionToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(replyTimeoutMilliseconds));
            stageTimer.Restart();
            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(_detectEndpoint,
                new DetectRequest
                {
                    Image = imageBase64,
                    FileName = work.FileName,
                }, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"HTTP {(int)response.StatusCode} ({response.StatusCode}); {responseBody}",
                    null, response.StatusCode);
            }

            stage = "响应JSON解析";
            DetectResult? result = await response.Content.ReadFromJsonAsync<DetectResult>(cancellationToken: timeout.Token).ConfigureAwait(false);
            httpMilliseconds = stageTimer.Elapsed.TotalMilliseconds;
            if (!IsCurrentSession(work.SessionGeneration)) return;
            if (result is null || !result.Ok)
            {
                string detail = result is null
                    ? "API返回空响应。"
                    : $"API检测失败 [{result.ErrorCode ?? "未知错误码"}]：{result.Error ?? "未提供错误详情。"}";
                string failure = $"{detail} API算法 {result?.TimeMs ?? 0:F1}ms，Base64 {base64Milliseconds:F1}ms，HTTP往返及解析 {httpMilliseconds:F1}ms，检测队列等待 {Stopwatch.GetElapsedTime(work.QueuedAtTimestamp).TotalMilliseconds - total.Elapsed.TotalMilliseconds:F1}ms。";
                DetectionFailed?.Invoke(failure);
                ReportFailure($"RingInspect 检测返回失败：{failure}");
                return;
            }

            Interlocked.Exchange(ref _failureNotified, 0);
            DetectionCompleted?.Invoke(result.DefectCount);
            Logger.Info("RingInspect 检测完成：{0}，缺陷数 {1}，API算法 {2:F1}ms，Base64 {3:F1}ms，HTTP往返及解析 {4:F1}ms，检测队列等待 {5:F1}ms。",
                result.Verdict ?? "完成", result.DefectCount, result.TimeMs, base64Milliseconds, httpMilliseconds,
                Stopwatch.GetElapsedTime(work.QueuedAtTimestamp).TotalMilliseconds - total.Elapsed.TotalMilliseconds);
            bool triggerDefect = false;
            if (result.DefectCount > 0)
            {
                lock (_sessionSync)
                {
                    if (_active && work.SessionGeneration == Volatile.Read(ref _sessionGeneration) && Interlocked.Exchange(ref _defectHandled, 1) == 0)
                    {
                        _active = false;
                        Interlocked.Increment(ref _sessionGeneration);
                        _activeSessionCancellation?.Cancel();
                        _activeSessionCancellation = null;
                        triggerDefect = true;
                    }
                }
            }
            if (triggerDefect) await _onDefect().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested || !IsCurrentSession(work.SessionGeneration)) { }
        catch (OperationCanceledException exception) when (timeout?.IsCancellationRequested == true && IsCurrentSession(work.SessionGeneration))
        {
            string message = $"检测超时：{stage}阶段超过 {Math.Max(100, _settings.VisionSoftwareReplyTimeoutMs)}ms。";
            DetectionFailed?.Invoke(message);
            ReportFailure($"{message} 总耗时 {total.Elapsed.TotalMilliseconds:F1}ms，Base64 {base64Milliseconds:F1}ms，当前阶段 {stageTimer.Elapsed.TotalMilliseconds:F1}ms，检测队列等待 {Stopwatch.GetElapsedTime(work.QueuedAtTimestamp).TotalMilliseconds - total.Elapsed.TotalMilliseconds:F1}ms。{exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            string message = $"API HTTP调用失败（{stage}，HTTP {exception.StatusCode?.ToString() ?? "无状态码"}）：{exception.Message}；总耗时 {total.Elapsed.TotalMilliseconds:F1}ms，Base64 {base64Milliseconds:F1}ms，HTTP阶段 {stageTimer.Elapsed.TotalMilliseconds:F1}ms。";
            DetectionFailed?.Invoke(message);
            ReportFailure(message);
        }
        catch (Exception exception)
        {
            if (IsCurrentSession(work.SessionGeneration))
            {
                string message = $"检测失败（{stage}）：{exception.GetType().Name}: {exception.Message}";
                DetectionFailed?.Invoke(message);
                ReportFailure(message);
            }
        }
        finally
        {
            timeout?.Dispose();
        }
    }
    private bool IsCurrentSession(int generation) =>
        _active && generation == Volatile.Read(ref _sessionGeneration);

    private async Task EnsureApiStartedAsync()
    {
        if (_detectEndpoint is not null) return;

        WebApplication app = RingInspectApiHost.Create([], "http://127.0.0.1:0");
        app.Use(async (context, next) =>
        {
            if (context.Request.Path != "/api/detect/strict")
            {
                await next(context);
                return;
            }

            long queuedAt = Stopwatch.GetTimestamp();
            try { await _apiRequestGate.WaitAsync(context.RequestAborted); }
            catch (OperationCanceledException) { return; }

            double waitMilliseconds = Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds;
            Stopwatch processing = Stopwatch.StartNew();
            try { await next(context); }
            finally
            {
                Logger.Info("RingInspect 请求进入 API：排队 {0:F1}ms，服务端处理 {1:F1}ms，请求已取消 {2}。",
                    waitMilliseconds, processing.Elapsed.TotalMilliseconds, context.RequestAborted.IsCancellationRequested);
                _apiRequestGate.Release();
            }
        });
        try
        {
            await app.StartAsync(_shutdown.Token).ConfigureAwait(false);
            IServer server = app.Services.GetRequiredService<IServer>();
            string? address = server.Features.Get<IServerAddressesFeature>()?.Addresses
                .FirstOrDefault(value => value.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? baseAddress))
                throw new InvalidOperationException("无法获取 RingInspect 本机服务地址。");

            using CancellationTokenSource healthTimeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            healthTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            using HttpResponseMessage healthResponse = await _httpClient.GetAsync(
                new Uri(baseAddress, "/api/health"), healthTimeout.Token).ConfigureAwait(false);
            healthResponse.EnsureSuccessStatusCode();
            using JsonDocument health = await JsonDocument.ParseAsync(
                await healthResponse.Content.ReadAsStreamAsync(healthTimeout.Token).ConfigureAwait(false),
                cancellationToken: healthTimeout.Token).ConfigureAwait(false);
            if (!health.RootElement.TryGetProperty("service", out JsonElement name) || name.GetString() != "RingInspect.Api")
                throw new InvalidOperationException("RingInspect 健康检查返回了非预期服务。");

            _apiHost = app;
            _detectEndpoint = new Uri(baseAddress, "/api/detect/strict");
            Logger.Info("RingInspect API 已在进程内启动：{0}", baseAddress);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void ReportFailure(string message)
    {
        if (Interlocked.Exchange(ref _failureNotified, 1) == 0) Report(message);
        else Logger.Warn(message);
    }

    private void Report(string message)
    {
        Logger.Warn(message);
        _warning(message);
    }

    public async ValueTask DisposeAsync()
    {
        await EndAsync().ConfigureAwait(false);
        _shutdown.Cancel();
        _detectionQueue.Writer.TryComplete();
        try { await _detectionWorker.ConfigureAwait(false); }
        catch (OperationCanceledException) { }

        foreach (CancellationTokenSource sessionCancellation in _sessionCancellations)
        {
            sessionCancellation.Dispose();
        }
        _sessionCancellations.Clear();

        if (_apiHost is not null)
        {
            using CancellationTokenSource stopTimeout = new(TimeSpan.FromSeconds(2));
            try { await _apiHost.StopAsync(stopTimeout.Token).ConfigureAwait(false); }
            catch (Exception exception) { Logger.Warn(exception, "停止 RingInspect API 时发生异常。"); }
            await _apiHost.DisposeAsync().ConfigureAwait(false);
        }
        _httpClient.Dispose();
        _shutdown.Dispose();
        _lifecycleGate.Dispose();
        _apiRequestGate.Dispose();
    }

    private sealed record DetectionWork(byte[] ImageBytes, string FileName, int SessionGeneration, long QueuedAtTimestamp, CancellationToken SessionToken);
}