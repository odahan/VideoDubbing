using System.Globalization;
using System.Text.RegularExpressions;
using LocalDub.Models;
using LocalDub.Utils;
using Microsoft.Extensions.Logging;

namespace LocalDub.Services;

/// <summary>Aligns a zero-start opening cue to the end of the source's leading silence.</summary>
public sealed partial class InitialSpeechAlignment(
    ToolPaths tools, ProcessRunner processes, ILogger<InitialSpeechAlignment> logger)
{
    /// <summary>Moves the opening cue only when a detectable silent lead-in ends within that cue.</summary>
    public async Task<IReadOnlyList<DubSegment>> AlignAsync(
        IReadOnlyList<DubSegment> segments, string sourceAudio, double videoDuration, CancellationToken cancellationToken)
    {
        if (segments.Count == 0 || segments[0].StartMilliseconds != 0)
        {
            return segments;
        }

        var first = segments[0];
        var slotEnd = SegmentTiming.GetAvailableSeconds(segments, 0, videoDuration);
        var scanEnd = Math.Min(first.EndMilliseconds / 1000d, slotEnd);
        if (scanEnd < 0.5)
        {
            return segments;
        }
        var result = await processes.RunAsync(tools.Ffmpeg,
        [
            "-hide_banner", "-nostats", "-t", scanEnd.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", sourceAudio, "-af", "silencedetect=noise=-45dB:duration=0.25", "-f", "null", "-"
        ], cancellationToken: cancellationToken);

        var onset = ReadLeadingSilenceEnd(result.StandardError, scanEnd);
        if (onset is null)
        {
            return segments;
        }

        var adjusted = segments.ToArray();
        adjusted[0] = new DubSegment
        {
            Id = first.Id,
            StartMilliseconds = onset.Value,
            EndMilliseconds = first.EndMilliseconds,
            SourceText = first.SourceText,
            SourceCueIds = first.SourceCueIds,
            Translation = first.Translation
        };
        logger.LogInformation("Aligned the opening cue to the source at {StartMs} ms", onset.Value);
        return adjusted;
    }

    /// <summary>Rejects pauses after speech and scans that end while still silent.</summary>
    internal static long? ReadLeadingSilenceEnd(string diagnostics, double scanEnd)
    {
        var start = SilenceStart().Match(diagnostics);
        if (!start.Success || !TryReadSeconds(start, out var startSeconds) || Math.Abs(startSeconds) > 0.02)
        {
            return null;
        }
        var end = SilenceEnd().Match(diagnostics, start.Index + start.Length);
        if (!end.Success || !TryReadSeconds(end, out var endSeconds) ||
            endSeconds < 0.25 || endSeconds > scanEnd - 0.25)
        {
            return null;
        }
        return (long)Math.Round(endSeconds * 1000);
    }

    private static bool TryReadSeconds(Match match, out double seconds) =>
        double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);

    [GeneratedRegex(@"silence_start:\s*(-?\d+(?:\.\d+)?)")]
    private static partial Regex SilenceStart();

    [GeneratedRegex(@"silence_end:\s*(-?\d+(?:\.\d+)?)")]
    private static partial Regex SilenceEnd();
}
