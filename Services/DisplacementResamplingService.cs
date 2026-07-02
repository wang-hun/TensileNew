using System.Globalization;
using TensileNeW.Models;
using TensileNeW.Tools;

namespace TensileNeW.Services;

public static class DisplacementResamplingService
{
    public const double SamplesPerSecond = 20d;
    public const double DefaultSpeed = 1d;
    public const double DefaultDisplacementStep = 0.05d;

    private const double DuplicateDistanceTolerance = 0.000001d;

    public sealed record ResampledLoadPoint(
        int Index,
        double RealDistance,
        double RealForce,
        double RealPress,
        double? Time);

    private sealed record SourcePoint(
        double RealDistance,
        double RealForce,
        double RealPress,
        double? Time);

    public static double GetDisplacementStep(double speed)
    {
        if (speed <= 0 || double.IsNaN(speed) || double.IsInfinity(speed))
        {
            speed = DefaultSpeed;
        }

        return speed / SamplesPerSecond;
    }

    public static IReadOnlyList<ResampledLoadPoint> ResampleByDisplacement(
        IEnumerable<Loadmodel> source,
        double displacementStep = DefaultDisplacementStep)
    {
        if (displacementStep <= 0 || double.IsNaN(displacementStep) || double.IsInfinity(displacementStep))
        {
            throw new ArgumentOutOfRangeException(nameof(displacementStep), "Displacement step must be greater than zero.");
        }

        List<SourcePoint> points = source
            .Select(ToSourcePoint)
            .Where(point => IsFinite(point.RealDistance) && IsFinite(point.RealForce))
            .OrderBy(point => point.RealDistance)
            .ToList();

        points = MergeDuplicateDistances(points);
        if (points.Count == 0)
        {
            return [];
        }

        if (points.Count == 1)
        {
            SourcePoint only = points[0];
            return
            [
                new ResampledLoadPoint(
                    1,
                    RoundDistance(only.RealDistance),
                    only.RealForce,
                    only.RealPress,
                    only.Time)
            ];
        }

        double start = Math.Ceiling(points[0].RealDistance / displacementStep) * displacementStep;
        double end = Math.Floor(points[^1].RealDistance / displacementStep) * displacementStep;
        if (start > end)
        {
            return [];
        }

        List<ResampledLoadPoint> result = [];
        int segmentIndex = 0;
        int outputIndex = 1;

        for (double distance = start; distance <= end + displacementStep * 0.5d; distance += displacementStep)
        {
            while (segmentIndex < points.Count - 2 &&
                   points[segmentIndex + 1].RealDistance < distance)
            {
                segmentIndex++;
            }

            SourcePoint left = points[segmentIndex];
            SourcePoint right = points[segmentIndex + 1];
            double ratio = (distance - left.RealDistance) / (right.RealDistance - left.RealDistance);
            ratio = Math.Clamp(ratio, 0d, 1d);

            result.Add(new ResampledLoadPoint(
                outputIndex++,
                RoundDistance(distance),
                Interpolate(left.RealForce, right.RealForce, ratio),
                Interpolate(left.RealPress, right.RealPress, ratio),
                InterpolateNullable(left.Time, right.Time, ratio)));
        }

        return result;
    }

    public static void SaveResampledDataToFile(
        string fileName,
        IEnumerable<Loadmodel> source,
        double displacementStep = DefaultDisplacementStep)
    {
        IReadOnlyList<ResampledLoadPoint> points = ResampleByDisplacement(source, displacementStep);

        using var exporter = new ExcelExporter_EPPlus();
        exporter.CreateSheet("算法整合数据")
            .SetHeader(new[] { "序号", "位移(mm)", "力(kN)", "压边(kN)", "时间(s)" })
            .AddData(points, point => new object[]
            {
                point.Index,
                point.RealDistance,
                point.RealForce,
                point.RealPress,
                point.Time?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty
            })
            .SaveToFile(fileName);
    }

    private static SourcePoint ToSourcePoint(Loadmodel source)
    {
        return new SourcePoint(
            source.RealDistance,
            source.RealForce,
            source.RealPress,
            TryParseTime(source.Time));
    }

    private static List<SourcePoint> MergeDuplicateDistances(List<SourcePoint> points)
    {
        if (points.Count <= 1)
        {
            return points;
        }

        List<SourcePoint> merged = [];
        int index = 0;

        while (index < points.Count)
        {
            SourcePoint current = points[index];
            int count = 1;
            double force = current.RealForce;
            double press = current.RealPress;
            double time = current.Time ?? 0d;
            int timeCount = current.Time.HasValue ? 1 : 0;
            int next = index + 1;

            while (next < points.Count &&
                   Math.Abs(points[next].RealDistance - current.RealDistance) <= DuplicateDistanceTolerance)
            {
                SourcePoint duplicate = points[next];
                force += duplicate.RealForce;
                press += duplicate.RealPress;
                if (duplicate.Time.HasValue)
                {
                    time += duplicate.Time.Value;
                    timeCount++;
                }

                count++;
                next++;
            }

            merged.Add(new SourcePoint(
                current.RealDistance,
                force / count,
                press / count,
                timeCount > 0 ? time / timeCount : null));

            index = next;
        }

        return merged;
    }

    private static double? TryParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariantValue))
        {
            return invariantValue;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double currentValue)
            ? currentValue
            : null;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static double Interpolate(double left, double right, double ratio)
    {
        return left + (right - left) * ratio;
    }

    private static double? InterpolateNullable(double? left, double? right, double ratio)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return left ?? right;
        }

        return Interpolate(left.Value, right.Value, ratio);
    }

    private static double RoundDistance(double value)
    {
        return Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }
}
