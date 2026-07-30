using System.Globalization;
using System.Text.RegularExpressions;
using LocalDub.Models;

namespace LocalDub.Services;

public static partial class SrtParser
{
    [GeneratedRegex(@"(?ms)^\s*(?<id>\d+)\s*\r?\n(?<start>\d{2}:\d{2}:\d{2}[,.]\d{3})\s*-->\s*(?<end>\d{2}:\d{2}:\d{2}[,.]\d{3})[^\r\n]*\r?\n(?<text>.*?)(?=\r?\n\s*\r?\n|\z)")]
    private static partial Regex EntryRegex();

    public static IReadOnlyList<DubSegment> Parse(string content)
    {
        return EntryRegex().Matches(content)
            .Select((match, index) => new DubSegment
            {
                Id = index + 1,
                StartMilliseconds = ParseTimestamp(match.Groups["start"].Value),
                EndMilliseconds = ParseTimestamp(match.Groups["end"].Value),
                SourceText = Regex.Replace(match.Groups["text"].Value, @"\s+", " ").Trim()
            })
            .Where(segment => !string.IsNullOrWhiteSpace(segment.SourceText))
            .ToList();
    }

    private static long ParseTimestamp(string value)
    {
        var normalized = value.Replace(',', '.');
        return (long)TimeSpan.ParseExact(normalized, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture).TotalMilliseconds;
    }
}
