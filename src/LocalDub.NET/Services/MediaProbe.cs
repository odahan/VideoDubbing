using System.Text.Json;
using LocalDub.Models;
using LocalDub.Utils;

namespace LocalDub.Services;

public sealed class MediaProbe(ToolPaths tools, ProcessRunner processRunner)
{
    public async Task<MediaInfo> ProbeAsync(string mediaPath, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(tools.Ffprobe,
        [
            "-v", "error",
            "-select_streams", "a:0",
            "-show_entries", "format=duration:stream=sample_rate,channels",
            "-of", "json",
            mediaPath
        ], cancellationToken: cancellationToken);

        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        var duration = double.Parse(root.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var stream = root.GetProperty("streams").EnumerateArray().FirstOrDefault();
        var rate = stream.ValueKind == JsonValueKind.Undefined ? 48000 : int.Parse(stream.GetProperty("sample_rate").GetString()!);
        var channels = stream.ValueKind == JsonValueKind.Undefined ? 2 : stream.GetProperty("channels").GetInt32();
        return new MediaInfo(duration, rate, channels);
    }

    public async Task<double> GetAudioDurationAsync(string audioPath, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(tools.Ffprobe,
        [
            "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", audioPath
        ], cancellationToken: cancellationToken);

        return double.Parse(result.StandardOutput.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
