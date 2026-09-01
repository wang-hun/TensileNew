using System.Net.Sockets;
using System.IO;
using System.Threading.Channels;
using System.Text;
using System.Threading;

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
    private CancellationTokenSource? _connectionClosedCancellation;
    private Task? _senderTask;
    private Task? _receiverTask;
    private bool _pendingNgPrefix;
    private int _disconnectNotified;
    private readonly Channel<string> _receivedMessageQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = true,
        AllowSynchronousContinuations = false
    });

    public event Action<string>? MessageReceived;
    public event Action? ConnectionClosed;
    public event Action? ConnectionStateChanged;

    public bool IsConnected
    {
        get
        {
            TcpClient? client = _tcpClient;
            Socket? socket = client?.Client;
            return client?.Connected == true
                && socket is not null
                && !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0)
                && _networkStream is not null;
        }
    }

    public async Task<bool> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535) return false;
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync(notifyStateChanged: false).ConfigureAwait(false);
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            TcpClient client = new();
            try
            {
                await client.ConnectAsync(host.Trim(), port, timeoutCts.Token).ConfigureAwait(false);
                client.NoDelay = true;
                _tcpClient = client;
                _networkStream = client.GetStream();
                _workerCancellation = new CancellationTokenSource();
                _connectionClosedCancellation = new CancellationTokenSource();
                Interlocked.Exchange(ref _disconnectNotified, 0);
                _pendingNgPrefix = false;
                CancellationToken workerToken = _workerCancellation.Token;
                _senderTask = Task.Run(() => SendLoopAsync(workerToken), workerToken);
                _receiverTask = Task.Run(() => ReceiveLoopAsync(workerToken), workerToken);
                ConnectionStateChanged?.Invoke();
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
        CancellationToken connectionToken = _connectionClosedCancellation?.Token ?? new CancellationToken(canceled: true);
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            connectionToken);
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
        catch (IOException) { HandleUnexpectedDisconnect(); }
        catch (ObjectDisposedException) { HandleUnexpectedDisconnect(); }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NetworkStream? stream = _networkStream;
                if (stream is null) return;
                int count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    HandleUnexpectedDisconnect();
                    return;
                }

                PublishTokens(Encoding.ASCII.GetString(buffer, 0, count));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { HandleUnexpectedDisconnect(); }
        catch (ObjectDisposedException) { HandleUnexpectedDisconnect(); }
    }

    private void PublishTokens(string chunk)
    {
        for (int i = 0; i < chunk.Length; i++)
        {
            char c = chunk[i];
            if (char.IsWhiteSpace(c) || c == '\0')
            {
                continue;
            }

            c = char.ToUpperInvariant(c);
            if (_pendingNgPrefix)
            {
                _pendingNgPrefix = false;
                if (c == 'G')
                {
                    PublishMessage("NG");
                    continue;
                }

                PublishMessage("N");
                if (c == 'N')
                {
                    _pendingNgPrefix = true;
                    continue;
                }
            }

            if (c == 'N')
            {
                _pendingNgPrefix = true;
                continue;
            }

            PublishMessage(c.ToString());
        }
    }

    private void PublishMessage(string message)
    {
        if (string.Equals(message, "NG", StringComparison.OrdinalIgnoreCase))
        {
            MessageReceived?.Invoke(message);
            return;
        }

        _receivedMessageQueue.Writer.TryWrite(message);
        MessageReceived?.Invoke(message);
    }

    private void HandleUnexpectedDisconnect()
    {
        if (Interlocked.Exchange(ref _disconnectNotified, 1) != 0)
        {
            return;
        }

        _workerCancellation?.Cancel();
        _connectionClosedCancellation?.Cancel();
        _networkStream = null;
        _tcpClient = null;
        ConnectionClosed?.Invoke();
        ConnectionStateChanged?.Invoke();
    }

    private async Task DisconnectCoreAsync(bool notifyStateChanged = true)
    {
        Interlocked.Exchange(ref _disconnectNotified, 1);
        _pendingNgPrefix = false;
        Task? senderTask = _senderTask;
        Task? receiverTask = _receiverTask;
        _senderTask = null;
        _receiverTask = null;
        CancellationTokenSource? workerCancellation = _workerCancellation;
        _workerCancellation = null;
        CancellationTokenSource? connectionClosedCancellation = _connectionClosedCancellation;
        _connectionClosedCancellation = null;
        NetworkStream? networkStream = _networkStream;
        _networkStream = null;
        TcpClient? tcpClient = _tcpClient;
        _tcpClient = null;

        workerCancellation?.Cancel();
        connectionClosedCancellation?.Cancel();
        try { networkStream?.Dispose(); } catch { }
        try { tcpClient?.Client?.Shutdown(SocketShutdown.Both); } catch { }
        try { tcpClient?.Dispose(); } catch { }

        if (senderTask is not null) { try { await senderTask.ConfigureAwait(false); } catch { } }
        if (receiverTask is not null) { try { await receiverTask.ConfigureAwait(false); } catch { } }

        workerCancellation?.Dispose();
        connectionClosedCancellation?.Dispose();
        while (_messageQueue.Reader.TryRead(out _)) { }
        if (notifyStateChanged)
        {
            ConnectionStateChanged?.Invoke();
        }
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
