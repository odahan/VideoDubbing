using System.Text;
using LocalDub.Models;

namespace LocalDub.Services;

public sealed class SubtitleGenerator
{
    public async Task GenerateAsync(IEnumerable<DubSegment> segments, string outputPath, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var index = 1;
        foreach (var segment in segments)
        {
            builder.AppendLine(index++.ToString());
            builder.Append(FormatTimestamp(segment.StartMilliseconds));
            builder.Append(" --> ");
            builder.AppendLine(FormatTimestamp(segment.EndMilliseconds));
            builder.AppendLine(segment.Translation.Trim());
            builder.AppendLine();
        }

        await File.WriteAllTextAsync(outputPath, builder.ToString(), new UTF8Encoding(false), cancellationToken);
    }

    public static string FormatTimestamp(long milliseconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}";
    }
}
