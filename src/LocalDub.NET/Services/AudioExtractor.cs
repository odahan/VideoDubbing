using LocalDub.Configuration;
using LocalDub.Utils;

namespace LocalDub.Services;

/// <summary>
/// Extracts audio tracks from a source video using FFmpeg, producing either a mono 16 kHz WAV
/// suitable for transcription or a full-quality stereo WAV suitable for final mixing.
/// </summary>
public sealed class AudioExtractor(ToolPaths tools, ProcessRunner processRunner, AppSettings settings)
{
    /// <summary>Extracts a mono, 16 kHz PCM WAV file optimized for speech transcription.</summary>
    public Task ExtractForTranscriptionAsync(string inputVideo, string outputWav, CancellationToken cancellationToken) =>
        processRunner.RunAsync(tools.Ffmpeg,
        [
            "-hide_banner", "-loglevel", "error", "-y", "-i", inputVideo,
            "-map", "0:a:0", "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", outputWav
        ], cancellationToken: cancellationToken);

    /// <summary>Extracts a stereo PCM WAV file at the configured sample rate/bit depth, used as the original mix for ducking/separation.</summary>
    public Task ExtractForMixingAsync(string inputVideo, string outputWav, CancellationToken cancellationToken) =>
        processRunner.RunAsync(tools.Ffmpeg,
        [
            "-hide_banner", "-loglevel", "error", "-y", "-i", inputVideo,
            "-map", "0:a:0", "-vn", "-ac", "2", "-ar", settings.Audio.SampleRate.ToString(),
            "-c:a", settings.Audio.BitDepth == 24 ? "pcm_s24le" : "pcm_s16le", outputWav
        ], cancellationToken: cancellationToken);
}
