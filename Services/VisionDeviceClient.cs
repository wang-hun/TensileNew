using System.Net.Sockets;
using System.IO;
using System.Threading.Channels;
using System.Text;

namespace TensileNeW.Services;

public sealed class VisionDeviceClient : IAsyncDisposable
{
    private readonly Channel<VisionDeviceMessage> _messageQueue = Channel.CreateUnbounded<VisionDeviceMessage>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private CancellationTokenSource? _workerCancellation;
    private Task? _senderTask;
    private Task? _receiverTask;
    private readonly Channel<string> _receivedMessageQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = true,
        AllowSynchronousContinuations = false
    });

    public event Action<string>? MessageReceived;

    public bool IsConnected => _tcpClient?.Connected == true && _networkStream is not null;

    public async Task<bool> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535) return false;
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            TcpClient client = new();
            try
            {
                await client.ConnectAsync(host.Trim(), port, timeoutCts.Token).ConfigureAwait(false);
                _tcpClient = client;
                _networkStream = client.GetStream();
                _workerCancellation = new CancellationTokenSource();
                _senderTask = Task.Run(() => SendLoopAsync(_workerCancellation.Token));
                _receiverTask = Task.Run(() => ReceiveLoopAsync(_workerCancellation.Token));
                return true;
            }
            catch
            {
                client.Dispose();
                return false;
            }
        }
        finally { _connectionLock.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await _connectionLock.WaitAsync().ConfigureAwait(false);
        try { await DisconnectCoreAsync().ConfigureAwait(false); }
        finally { _connectionLock.Release(); }
    }

    public bool TryEnqueue(VisionDeviceMessage message) => IsConnected && _messageQueue.Writer.TryWrite(message);

    public async ValueTask<string> WaitForMessageAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        return await _receivedMessageQueue.Reader.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (VisionDeviceMessage message in _messageQueue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                NetworkStream? stream = _networkStream;
                if (stream is null) continue;
                await stream.WriteAsync(message.Payload, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1024];
        StringBuilder pending = new();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NetworkStream? stream = _networkStream;
                if (stream is null) return;
                int count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0) return;
                pending.Append(Encoding.UTF8.GetString(buffer, 0, count));
                PublishTokens(pending);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private void PublishTokens(StringBuilder pending)
    {
        string text = pending.ToString().Replace("\r", string.Empty, StringComparison.Ordinal);
        pending.Clear();
        string[] chunks = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string chunk in chunks)
        {
            string message = chunk.ToUpperInvariant();
            _receivedMessageQueue.Writer.TryWrite(message);
            MessageReceived?.Invoke(message);
        }
    }

    private async Task DisconnectCoreAsync()
    {
        _workerCancellation?.Cancel();
        if (_senderTask is not null) { try { await _senderTask.ConfigureAwait(false); } catch { } }
        _senderTask = null;
        if (_receiverTask is not null) { try { await _receiverTask.ConfigureAwait(false); } catch { } }
        _receiverTask = null;
        _workerCancellation?.Dispose();
        _workerCancellation = null;
        _networkStream?.Dispose();
        _networkStream = null;
        _tcpClient?.Dispose();
        _tcpClient = null;
        while (_messageQueue.Reader.TryRead(out _)) { }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _connectionLock.Dispose();
    }
}

public sealed record VisionDeviceMessage(ReadOnlyMemory<byte> Payload)
{
    public static VisionDeviceMessage FromText(string text) =>
        new(System.Text.Encoding.UTF8.GetBytes(text));
}
