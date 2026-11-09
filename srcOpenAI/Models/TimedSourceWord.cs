using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalDub.Models;

/// <summary>A source word reconstructed from Whisper's timed text tokens.</summary>
internal readonly partial record struct TimedSourceWord(string Text, long StartMilliseconds, long EndMilliseconds)
{
    /// <summary>Extracts comparable words without changing the source text used for translation.</summary>
    internal static MatchCollection MatchWords(string text) => WordPattern().Matches(text);

    /// <summary>Combines consecutive subword tokens into the words used for cue alignment.</summary>
    internal static IReadOnlyList<TimedSourceWord> Read(JsonElement document)
    {
        var words = new List<TimedSourceWord>();
        if (!document.TryGetProperty("transcription", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return words;
        }
        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            var text = new StringBuilder();
            var ranges = new List<(int End, long StartMs, long EndMs)>();
            foreach (var token in tokens.EnumerateArray())
            {
                var piece = token.GetProperty("text").GetString();
                if (string.IsNullOrEmpty(piece) || piece.StartsWith("[_", StringComparison.Ordinal))
                {
                    continue;
                }
                var offsets = token.GetProperty("offsets");
                text.Append(piece);
                ranges.Add((text.Length, offsets.GetProperty("from").GetInt64(), offsets.GetProperty("to").GetInt64()));
            }

            var tokenIndex = 0;
            foreach (Match word in MatchWords(text.ToString()))
            {
                while (ranges[tokenIndex].End <= word.Index)
                {
                    tokenIndex++;
                }
                var start = ranges[tokenIndex].StartMs;
                while (ranges[tokenIndex].End < word.Index + word.Length)
                {
                    tokenIndex++;
                }
                words.Add(new TimedSourceWord(word.Value, start, ranges[tokenIndex].EndMs));
            }
        }
        return words.OrderBy(word => word.StartMilliseconds).ToArray();
    }

    [GeneratedRegex(@"[\p{L}\p{M}\p{Nd}]+")]
    private static partial Regex WordPattern();
}
