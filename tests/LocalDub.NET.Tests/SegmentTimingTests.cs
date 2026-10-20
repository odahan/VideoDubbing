using LocalDub.Models;
using LocalDub.Services;

namespace LocalDub.NET.Tests;

public sealed class SegmentTimingTests
{
    [Fact]
    public void OverlappingSubtitleCannotExtendAudioBeyondNextStart()
    {
        DubSegment[] segments =
        [
            new() { Id = 1, StartMilliseconds = 1000, EndMilliseconds = 5000 },
            new() { Id = 2, StartMilliseconds = 3000, EndMilliseconds = 6000 }
        ];

        Assert.Equal(1.98, SegmentTiming.GetAvailableSeconds(segments, 0, 8), precision: 5);
        Assert.Equal(4.98, SegmentTiming.GetAvailableSeconds(segments, 1, 8), precision: 5);
    }

    [Theory]
    [InlineData(-1, 1000, 3)]
    [InlineData(1000, 1000, 3)]
    [InlineData(3000, 4000, 3)]
    [InlineData(0, 1000, 0)]
    public void RejectsInvalidTimestamps(long start, long end, double duration)
    {
        DubSegment[] segments = [new() { Id = 1, StartMilliseconds = start, EndMilliseconds = end }];
        Assert.Throws<InvalidDataException>(() => SegmentTiming.GetAvailableSeconds(segments, 0, duration));
    }

    [Fact]
    public void RejectsUnorderedStarts()
    {
        DubSegment[] segments =
        [
            new() { Id = 1, StartMilliseconds = 1000, EndMilliseconds = 2000 },
            new() { Id = 2, StartMilliseconds = 500, EndMilliseconds = 3000 }
        ];
        Assert.Throws<InvalidDataException>(() => SegmentTiming.GetAvailableSeconds(segments, 0, 4));
    }
}
