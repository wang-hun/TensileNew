using System.Net.Sockets;
using System.IO;
using System.Threading.Channels;

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

    private async Task DisconnectCoreAsync()
    {
        _workerCancellation?.Cancel();
        if (_senderTask is not null) { try { await _senderTask.ConfigureAwait(false); } catch { } }
        _senderTask = null;
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

public sealed record VisionDeviceMessage(ReadOnlyMemory<byte> Payload);
