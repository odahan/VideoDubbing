using LocalDub.Configuration;
using LocalDub.Utils;

namespace LocalDub.Services;

public sealed class AudioExtractor(ToolPaths tools, ProcessRunner processRunner, AppSettings settings)
{
    public Task ExtractForTranscriptionAsync(string inputVideo, string outputWav, CancellationToken cancellationToken) =>
        processRunner.RunAsync(tools.Ffmpeg,
        [
            "-hide_banner", "-loglevel", "error", "-y", "-i", inputVideo,
            "-map", "0:a:0", "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", outputWav
        ], cancellationToken: cancellationToken);

    public Task ExtractForMixingAsync(string inputVideo, string outputWav, CancellationToken cancellationToken) =>
        processRunner.RunAsync(tools.Ffmpeg,
        [
            "-hide_banner", "-loglevel", "error", "-y", "-i", inputVideo,
            "-map", "0:a:0", "-vn", "-ac", "2", "-ar", settings.Audio.SampleRate.ToString(),
            "-c:a", settings.Audio.BitDepth == 24 ? "pcm_s24le" : "pcm_s16le", outputWav
        ], cancellationToken: cancellationToken);
}
