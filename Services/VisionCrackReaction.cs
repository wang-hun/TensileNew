namespace TensileNeW.Services;

public sealed class VisionCrackReaction(Func<IReadOnlyList<VisionDefectBox>, Task> stopMotion)
{
    public Task HandleAsync(IReadOnlyList<VisionDefectBox> defects) => stopMotion(defects);
}