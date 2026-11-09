using LocalDub.Models;

namespace LocalDub.Services;

/// <summary>Validates segment placement and enforces the next segment's hard audio boundary.</summary>
public static class SegmentTiming
{
    /// <summary>Removes zero-duration cues whose text is already present in an adjacent timed cue.</summary>
    public static IReadOnlyList<DubSegment> RemoveRedundantZeroDurationSegments(IReadOnlyList<DubSegment> segments)
    {
        var result = new List<DubSegment>(segments.Count);
        for (var index = segments.Count - 1; index >= 0; index--)
        {
            var segment = segments[index];
            var previous = index > 0 ? segments[index - 1] : null;
            var next = result.Count > 0 ? result[^1] : null;
            if (segment.StartMilliseconds == segment.EndMilliseconds
                && (IsTimedDuplicate(segment, previous) || IsTimedDuplicate(segment, next)))
            {
                continue;
            }

            result.Add(segment);
        }

        result.Reverse();
        return result;
    }

    /// <summary>Validates every narration slot before translation or synthesis starts.</summary>
    public static void Validate(IReadOnlyList<DubSegment> segments, double videoDurationSeconds)
    {
        if (segments.Count == 0)
        {
            throw new InvalidDataException("No speech segments are available for synthesis.");
        }

        for (var index = 0; index < segments.Count; index++)
        {
            GetAvailableSeconds(segments, index, videoDurationSeconds);
        }
    }

    /// <summary>Returns the usable narration slot, rejecting invalid or unordered timestamps.</summary>
    public static double GetAvailableSeconds(IReadOnlyList<DubSegment> segments, int index, double videoDurationSeconds)
    {
        var segment = segments[index];
        var start = segment.StartMilliseconds / 1000d;
        var nextStart = index + 1 < segments.Count
            ? segments[index + 1].StartMilliseconds / 1000d
            : videoDurationSeconds;
        if (!double.IsFinite(videoDurationSeconds) || videoDurationSeconds <= 0
            || start < 0 || start >= videoDurationSeconds
            || segment.EndMilliseconds <= segment.StartMilliseconds
            || nextStart <= start || nextStart > videoDurationSeconds)
        {
            throw new InvalidDataException(
                $"Invalid timestamps for segment {segment.Id} (start {segment.StartMilliseconds} ms, " +
                $"end {segment.EndMilliseconds} ms, next boundary {nextStart * 1000:0.###} ms).");
        }

        var duration = nextStart - start;
        return duration > 0.02 ? duration - 0.02 : duration;
    }

    private static bool IsTimedDuplicate(DubSegment segment, DubSegment? adjacent) =>
        adjacent is not null
        && adjacent.EndMilliseconds > adjacent.StartMilliseconds
        && adjacent.StartMilliseconds <= segment.StartMilliseconds
        && adjacent.EndMilliseconds >= segment.StartMilliseconds
        && string.Equals(adjacent.SourceText, segment.SourceText, StringComparison.Ordinal);
}
