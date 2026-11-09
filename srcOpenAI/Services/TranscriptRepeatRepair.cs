using System.Globalization;
using System.Text.RegularExpressions;
using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Utils;
using Microsoft.Extensions.Logging;

namespace LocalDub.Services;

/// <summary>Checks suspicious repeated French cues against an independent local transcription.</summary>
public sealed partial class TranscriptRepeatRepair(
    AudioExtractor audioExtractor, ToolPaths tools, ProcessRunner processes, AppSettings settings,
    ILogger<TranscriptRepeatRepair> logger)
{
    /// <summary>Repairs confirmed loops while retaining the existing translations and original source files.</summary>
    public async Task<IReadOnlyList<DubSegment>> RepairAsync(
        IReadOnlyList<DubSegment> segments, string audioSource, string workDirectory, CancellationToken cancellationToken)
    {
        var regions = FindSuspiciousRegions(segments);
        if (regions.Count == 0)
        {
            return segments;
        }

        var wav = Path.Combine(workDirectory, "speech-16k.wav");
        if (!File.Exists(wav))
        {
            await audioExtractor.ExtractForTranscriptionAsync(audioSource, wav, cancellationToken);
        }
        logger.LogInformation("Verifying {Count} suspicious transcription loops with local Whisper", regions.Count);
        var result = new List<DubSegment>(segments);
        foreach (var (start, end) in regions.Reverse())
        {
            var originals = segments.Skip(start).Take(end - start).ToArray();
            var checkedSegments = await CheckRegionAsync(originals[0], originals[^1], wav, workDirectory, cancellationToken);
            var replacement = GetConfirmedCues(originals, checkedSegments);
            if (replacement.Count == 0)
            {
                logger.LogWarning("Could not verify a correction for repeated source cues {First}–{Last}", originals[0].Id, originals[^1].Id);
                continue;
            }

            result.RemoveRange(start, originals.Length);
            result.InsertRange(start, replacement);
            logger.LogInformation("Replaced {Original} repeated source cues with {Verified} verified cues around {StartMs} ms",
                originals.Length, replacement.Count, replacement[0].StartMilliseconds);
        }
        return result.Count == segments.Count ? segments : result;
    }

    private async Task<IReadOnlyList<DubSegment>> CheckRegionAsync(
        DubSegment first, DubSegment last, string wav, string workDirectory, CancellationToken cancellationToken)
    {
        var offset = Math.Max(0, first.StartMilliseconds - 16000);
        var duration = last.EndMilliseconds - offset + 8000;
        var prefix = Path.Combine(workDirectory, $"transcription-repeat-check-{first.Id}");
        await processes.RunAsync(tools.Whisper,
        [
            "-m", tools.WhisperModel, "-f", wav, "-l", settings.Whisper.Language,
            "-ot", offset.ToString(CultureInfo.InvariantCulture), "-d", duration.ToString(CultureInfo.InvariantCulture),
            "-t", settings.Whisper.Threads.ToString(CultureInfo.InvariantCulture), "-mc", "0", "-nf",
            "-ml", settings.Whisper.MaxSegmentCharacters.ToString(CultureInfo.InvariantCulture), "-sow", "-osrt", "-of", prefix
        ], cancellationToken: cancellationToken);
        return SrtParser.Parse(await File.ReadAllTextAsync(prefix + ".srt", cancellationToken));
    }

    /// <summary>Finds adjacent runs of long identical text with at least one implausibly short cue.</summary>
    internal static IReadOnlyList<(int Start, int End)> FindSuspiciousRegions(IReadOnlyList<DubSegment> segments)
    {
        var normalized = segments.Select(segment => Normalize(segment.SourceText)).ToArray();
        var regions = new List<(int Start, int End)>();
        for (var start = 0; start < segments.Count;)
        {
            var end = start + 1;
            while (end < segments.Count && normalized[end] == normalized[start] &&
                Math.Abs(segments[end].StartMilliseconds - segments[end - 1].EndMilliseconds) <= 250)
            {
                end++;
            }
            if (end - start >= 3 && Words().Matches(segments[start].SourceText).Count >= 6)
            {
                if (regions.Count > 0 && regions[^1].End == start &&
                    Math.Abs(segments[start].StartMilliseconds - segments[start - 1].EndMilliseconds) <= 250)
                {
                    regions[^1] = (regions[^1].Start, end);
                }
                else
                {
                    regions.Add((start, end));
                }
            }
            start = end;
        }
        return regions.Where(region => segments.Skip(region.Start).Take(region.End - region.Start)
            .Any(segment => Words().Matches(segment.SourceText).Count * 1000L >
                6 * (segment.EndMilliseconds - segment.StartMilliseconds))).ToArray();
    }

    /// <summary>Retains only verified timed copies when every removed wording is accounted for.</summary>
    internal static IReadOnlyList<DubSegment> GetConfirmedCues(
        IReadOnlyList<DubSegment> originals, IReadOnlyList<DubSegment> checkedSegments)
    {
        var byText = originals.GroupBy(cue => Normalize(cue.SourceText))
            .ToDictionary(group => group.Key, group => new Queue<DubSegment>(group));
        var verified = new List<DubSegment>();
        foreach (var cue in checkedSegments)
        {
            if (cue.StartMilliseconds < originals[0].StartMilliseconds ||
                cue.EndMilliseconds > originals[^1].EndMilliseconds ||
                cue.EndMilliseconds <= cue.StartMilliseconds ||
                !byText.TryGetValue(Normalize(cue.SourceText), out var matches))
            {
                continue;
            }
            if (matches.Count == 0)
            {
                return [];
            }
            var original = matches.Dequeue();
            verified.Add(new DubSegment
            {
                Id = original.Id,
                SourceText = original.SourceText,
                SourceCueIds = original.SourceCueIds,
                Translation = original.Translation,
                StartMilliseconds = cue.StartMilliseconds,
                EndMilliseconds = cue.EndMilliseconds
            });
        }
        if (verified.Count == 0 || verified.Count >= originals.Count ||
            byText.Keys.Any(text => !verified.Any(cue => IsSubsequence(text, Normalize(cue.SourceText)))))
        {
            return [];
        }
        return verified;
    }

    private static bool IsSubsequence(string shorter, string longer)
    {
        var words = shorter.Split(' ');
        var index = 0;
        foreach (var word in longer.Split(' '))
        {
            if (word == words[index] && ++index == words.Length)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Compares wording independently of typography and punctuation.</summary>
    internal static string Normalize(string text) =>
        string.Join(' ', Words().Matches(text).Select(match => match.Value.ToUpperInvariant()));

    [GeneratedRegex(@"[\p{L}\p{M}\p{Nd}]+")]
    private static partial Regex Words();
}
