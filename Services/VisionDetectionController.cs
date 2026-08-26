namespace TensileNeW.Services;

public enum VisionDetectionState
{
    Disabled,
    Disconnected,
    Closed,
    Opening,
    Open,
    Starting,
    WaitingForNg,
    StoppingForNg,
    Interrupted,
    Ending
}

/// <summary>
/// State machine for the independent vision device protocol.
/// Every command is sent through VisionDeviceClient's message queue.
/// </summary>
public sealed class VisionDetectionController : IAsyncDisposable
{
    private readonly VisionDeviceClient _client;
    private readonly Func<Task> _stopPlcAction;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VisionDetectionState _state = VisionDetectionState.Closed;
    private bool _collectionEnded;
    private bool _saveCompleted;

    public VisionDetectionController(VisionDeviceClient client, Func<Task> stopPlcAction)
    {
        _client = client;
        _stopPlcAction = stopPlcAction;
        _client.MessageReceived += OnMessageReceived;
    }

    public VisionDetectionState State => _state;

    public async Task OnTensileRequestedAsync()
    {
        if (!CanUse() || _state is not VisionDetectionState.Closed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state is not VisionDetectionState.Closed || !CanUse()) return;
            _state = VisionDetectionState.Opening;
            if (!await SendAndWaitAsync("R", "R").ConfigureAwait(false))
            {
                _state = VisionDetectionState.Closed;
                return;
            }
            _state = VisionDetectionState.Open;
        }
        finally { _gate.Release(); }
    }

    public async Task OnDataCollectionStartedAsync()
    {
        if (!CanUse()) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state is not VisionDetectionState.Open || !CanUse()) return;
            _state = VisionDetectionState.Starting;
            _collectionEnded = false;
            _saveCompleted = false;
            _state = await SendAndWaitAsync("A", "A").ConfigureAwait(false)
                ? VisionDetectionState.WaitingForNg
                : VisionDetectionState.Open;
        }
        finally { _gate.Release(); }
    }

    public async Task OnStopRequestedAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state is VisionDetectionState.WaitingForNg or VisionDetectionState.Starting)
            {
                _state = VisionDetectionState.Interrupted;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task OnDataCollectionEndedAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state is VisionDetectionState.WaitingForNg or VisionDetectionState.Interrupted or VisionDetectionState.StoppingForNg)
            {
                _collectionEnded = true;
                _state = VisionDetectionState.Ending;
                await TryFinishAsync().ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task OnSaveCompletedAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _saveCompleted = true;
            await TryFinishAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task TryFinishAsync()
    {
        if (!_collectionEnded || !_saveCompleted || _state is not VisionDetectionState.Ending) return;
        await SendAndWaitAsync("S", "S").ConfigureAwait(false);
        _state = VisionDetectionState.Closed;
        _collectionEnded = false;
        _saveCompleted = false;
    }

    private async Task<bool> SendAndWaitAsync(string command, string expectedReply)
    {
        if (!_client.TryEnqueue(VisionDeviceMessage.FromText(command))) return false;
        try
        {
            string reply = await _client.WaitForMessageAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            return string.Equals(reply, expectedReply, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { return false; }
    }

    private void OnMessageReceived(string message)
    {
        if (string.Equals(message, "NG", StringComparison.OrdinalIgnoreCase))
        {
            _ = HandleNgAsync();
        }
    }

    private async Task HandleNgAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state is not VisionDetectionState.WaitingForNg) return;
            _state = VisionDetectionState.StoppingForNg;
            await _stopPlcAction().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private static bool CanUse() => Models.RAM.IsVisionDetectionActive;

    public async ValueTask DisposeAsync()
    {
        _client.MessageReceived -= OnMessageReceived;
        _gate.Dispose();
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}

