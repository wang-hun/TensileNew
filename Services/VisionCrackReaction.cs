namespace TensileNeW.Services;

public sealed class VisionCrackReaction(Func<Task> stopMotion)
{
    public Task HandleAsync() => stopMotion();
}
