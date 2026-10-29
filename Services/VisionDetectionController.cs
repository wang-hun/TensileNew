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

[Obsolete("Legacy vision detection controller is retired. Use NewVisionProcessingService for the new entry point.")]
public sealed class VisionDetectionController : IAsyncDisposable
{
    public VisionDetectionController(VisionDeviceClient client, Func<Task> stopPlcAction)
    {
    }

    public VisionDetectionState State => VisionDetectionState.Disabled;

    public Task OnTensileRequestedAsync() => Task.CompletedTask;

    public Task OnDataCollectionStartedAsync() => Task.CompletedTask;

    public Task OnStopRequestedAsync() => Task.CompletedTask;

    public Task OnDataCollectionEndedAsync() => Task.CompletedTask;

    public Task OnSaveCompletedAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}