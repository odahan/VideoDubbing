using LocalDub.Models;

namespace LocalDub.Services;

/// <summary>Splits long subtitles within the rendered speech window without affecting the audio.</summary>
internal static class NarrationSubtitleLayout
{
    private const int LineLength = 42;

    /// <summary>Creates at most two lines per cue and allocates timing by text length within each voiced unit.</summary>
    internal static IEnumerable<DubSegment> Split(IEnumerable<DubSegment> spokenUnits)
    {
        foreach (var unit in spokenUnits)
        {
            var lines = WrapLines(unit.Translation);
            var cues = lines.Chunk(2).ToArray();
            var duration = unit.EndMilliseconds - unit.StartMilliseconds;
            if (cues.Length <= 1 || duration < cues.Length)
            {
                if (cues.Length == 1)
                {
                    yield return Subtitle(unit.StartMilliseconds, unit.EndMilliseconds, cues[0]);
                }
                else
                {
                    yield return unit;
                }
                continue;
            }

            var length = lines.Sum(line => line.Length);
            var consumed = 0;
            var start = unit.StartMilliseconds;
            for (var index = 0; index < cues.Length; index++)
            {
                consumed += cues[index].Sum(line => line.Length);
                var estimatedEnd = unit.StartMilliseconds + (long)Math.Round(duration * (double)consumed / length);
                var end = Math.Clamp(estimatedEnd, start + 1, unit.EndMilliseconds - (cues.Length - index - 1));
                yield return Subtitle(start, end, cues[index]);
                start = end;
            }
        }
    }

    private static List<string> WrapLines(string text)
    {
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > LineLength)
            {
                lines.Add(line);
                line = string.Empty;
            }
            line = line.Length == 0 ? word : $"{line} {word}";
        }
        if (line.Length > 0)
        {
            lines.Add(line);
        }
        return lines;
    }

    private static DubSegment Subtitle(long start, long end, string[] lines) => new()
    {
        StartMilliseconds = start,
        EndMilliseconds = end,
        Translation = string.Join(Environment.NewLine, lines)
    };
}
