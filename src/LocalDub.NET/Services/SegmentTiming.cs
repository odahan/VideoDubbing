using LocalDub.Models;

namespace LocalDub.Services;

/// <summary>Validates segment placement and enforces the next segment's hard audio boundary.</summary>
public static class SegmentTiming
{
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
            throw new InvalidDataException($"Invalid timestamps for segment {segment.Id}.");
        }

        var duration = nextStart - start;
        return duration > 0.02 ? duration - 0.02 : duration;
    }
}
