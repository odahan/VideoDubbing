using System.Text.RegularExpressions;
using LocalDub.Models;

namespace LocalDub.Services;

/// <summary>Turns source subtitle cues into timed units suitable for continuous narration.</summary>
internal static partial class NarrationSegmenter
{
    private const int PauseBoundaryMilliseconds = 1000;
    private const int MinimumPieceMilliseconds = 450;
    private const int PreferredMaximumMilliseconds = 32000;
    private const int PreferredMaximumCharacters = 600;
    private const int AlignmentToleranceMilliseconds = 800;
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
        { "etc", "ex", "cf", "mme", "mlle", "mm", "mr", "dr", "pr", "st", "ste", "av", "fig", "approx" };

    /// <summary>Indicates whether optional word timing could separate any sentences within a cue.</summary>
    internal static bool HasInternalSentenceBoundaries(IEnumerable<DubSegment> cues) =>
        cues.Any(cue => GetBoundaries(cue.SourceText.Trim()).Any(boundary =>
            boundary.End < cue.SourceText.Trim().Length && boundary.IsSentence));

    /// <summary>Merges continuous source fragments while retaining the start and end of each spoken unit.</summary>
    internal static IReadOnlyList<DubSegment> Group(
        IReadOnlyList<DubSegment> cues, IReadOnlyList<TimedSourceWord> words)
    {
        var result = new List<DubSegment>();
        var pending = new List<Piece>();
        foreach (var cue in cues)
        {
            foreach (var piece in SplitCue(cue, words))
            {
                if (pending.Count > 0 && (pending[^1].EndsSentence ||
                    piece.StartMs - pending[^1].EndMs >= PauseBoundaryMilliseconds))
                {
                    Emit(pending.Count);
                }

                pending.Add(piece);
                while (pending.Count > 1 && (pending[^1].EndMs - pending[0].StartMs > PreferredMaximumMilliseconds ||
                    pending.Sum(part => part.Text.Length + 1) > PreferredMaximumCharacters))
                {
                    var clauseEnd = pending.FindLastIndex(pending.Count - 2, part => part.EndsClause &&
                        part.EndMs - pending[0].StartMs >= PreferredMaximumMilliseconds / 2);
                    Emit(clauseEnd < 0 ? pending.Count - 1 : clauseEnd + 1);
                }
            }
        }
        if (pending.Count > 0)
        {
            Emit(pending.Count);
        }
        return result;

        void Emit(int count)
        {
            var parts = pending.GetRange(0, count);
            var start = parts[0].StartMs;
            var end = parts[^1].EndMs;
            result.Add(new DubSegment
            {
                Id = result.Count + 1,
                StartMilliseconds = start,
                EndMilliseconds = end,
                SourceText = string.Join(' ', parts.Select(part => part.Text)),
                SourceCueIds = parts.SelectMany(part => part.Cue.SourceCueIds ?? [part.Cue.Id]).Distinct().ToArray()
            });
            pending.RemoveRange(0, count);
        }
    }

    /// <summary>Returns the speech budget without borrowing the silence before the next unit.</summary>
    internal static double GetSpeechBudget(IReadOnlyList<DubSegment> segments, int index, double videoDuration)
    {
        var segment = segments[index];
        var next = index + 1 < segments.Count ? segments[index + 1].StartMilliseconds / 1000d : videoDuration;
        var end = Math.Min(segment.EndMilliseconds / 1000d, next);
        var duration = end - segment.StartMilliseconds / 1000d;
        return duration > 0.02 ? duration - 0.02 : duration;
    }

    private static IEnumerable<Piece> SplitCue(DubSegment cue, IReadOnlyList<TimedSourceWord> words)
    {
        var text = cue.SourceText.Trim();
        var boundaries = GetBoundaries(text).ToArray();
        var sourceWords = TimedSourceWord.MatchWords(text);
        var startWord = boundaries.Any(boundary => boundary.End < text.Length)
            ? FindCueWords(cue, sourceWords, words)
            : -1;
        var startMs = cue.StartMilliseconds;
        var position = 0;
        var wordIndex = 0;
        if (startWord >= 0)
        {
            foreach (var boundary in boundaries)
            {
                while (wordIndex < sourceWords.Count && sourceWords[wordIndex].Index < boundary.End)
                {
                    wordIndex++;
                }
                if (wordIndex == sourceWords.Count)
                {
                    break;
                }
                var splitMs = words[startWord + wordIndex].StartMilliseconds;
                if (splitMs - startMs < MinimumPieceMilliseconds || cue.EndMilliseconds - splitMs < MinimumPieceMilliseconds)
                {
                    continue;
                }

                yield return new Piece(cue, startMs, splitMs, text[position..boundary.End].Trim(),
                    boundary.IsSentence, true);
                position = boundary.End;
                startMs = splitMs;
            }
        }

        var endsSentence = boundaries.Any(boundary => boundary.End == text.Length && boundary.IsSentence);
        var endsClause = boundaries.Any(boundary => boundary.End == text.Length);
        yield return new Piece(cue, startMs, cue.EndMilliseconds, text[position..].Trim(), endsSentence, endsClause);
    }

    private static int FindCueWords(DubSegment cue, MatchCollection expected, IReadOnlyList<TimedSourceWord> words)
    {
        if (expected.Count == 0 || words.Count < expected.Count)
        {
            return -1;
        }

        var bestIndex = -1;
        var nearest = long.MaxValue;
        for (var index = LowerBound(words, cue.StartMilliseconds - AlignmentToleranceMilliseconds);
             index <= words.Count - expected.Count && words[index].StartMilliseconds < cue.EndMilliseconds;
             index++)
        {
            var last = words[index + expected.Count - 1];
            if (last.EndMilliseconds > cue.EndMilliseconds + AlignmentToleranceMilliseconds)
            {
                continue;
            }
            var distance = Math.Abs(words[index].StartMilliseconds - cue.StartMilliseconds);
            if (distance >= nearest || !expected.Select((word, offset) =>
                string.Equals(word.Value, words[index + offset].Text, StringComparison.OrdinalIgnoreCase)).All(match => match))
            {
                continue;
            }
            bestIndex = index;
            nearest = distance;
        }
        return bestIndex;
    }

    private static int LowerBound(IReadOnlyList<TimedSourceWord> words, long startMs)
    {
        var first = 0;
        var end = words.Count;
        while (first < end)
        {
            var middle = first + (end - first) / 2;
            if (words[middle].StartMilliseconds < startMs)
            {
                first = middle + 1;
            }
            else
            {
                end = middle;
            }
        }
        return first;
    }

    private static IEnumerable<(int End, bool IsSentence)> GetBoundaries(string text)
    {
        foreach (Match boundary in BoundaryPattern().Matches(text))
        {
            var mark = boundary.Groups["mark"].Value;
            if (mark == ".")
            {
                var preceding = TimedSourceWord.MatchWords(text[..boundary.Index]);
                if (preceding.Count > 0)
                {
                    var word = preceding[^1].Value;
                    if (Abbreviations.Contains(word) || (word.Length == 1 && char.IsUpper(word[0])))
                    {
                        continue;
                    }
                }
            }
            yield return (boundary.Index + boundary.Length, mark[0] is '.' or '!' or '?');
        }
    }

    [GeneratedRegex("""(?<mark>[.!?]+|[,;:])["'»”’)\]]*(?=\s|$)""")]
    private static partial Regex BoundaryPattern();

    private sealed record Piece(DubSegment Cue, long StartMs, long EndMs, string Text, bool EndsSentence, bool EndsClause);
}
