using System.Globalization;
using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Utils;
using Microsoft.Extensions.Logging;

namespace LocalDub.Services;

/// <summary>
/// Synthesizes each dub segment, fits it into its time slot (rewording or speeding up as needed),
/// and concatenates the results into a single dubbed WAV spanning the full video duration.
/// </summary>
public sealed class AudioTimelineBuilder(
    ToolPaths tools,
    ProcessRunner processRunner,
    MediaProbe mediaProbe,
    ChatterboxSynthesizer synthesizer,
    OllamaTranslator translator,
    AppSettings settings,
    ILogger<AudioTimelineBuilder> logger)
{
    /// <summary>
    /// Synthesizes, times and concatenates every segment into <paramref name="outputWav"/>, padded
    /// or trimmed to exactly <paramref name="videoDurationSeconds"/>.
    /// </summary>
    public async Task BuildAsync(
        IReadOnlyList<DubSegment> segments,
        VoiceProfile voice,
        string? ollamaModel,
        double videoDurationSeconds,
        string workDirectory,
        string outputWav,
        CancellationToken cancellationToken,
        Glossary? glossary = null)
    {
        if (segments.Count == 0)
        {
            throw new InvalidDataException("No speech segments are available for synthesis.");
        }
        for (var index = 0; index < segments.Count; index++)
        {
            SegmentTiming.GetAvailableSeconds(segments, index, videoDurationSeconds);
        }

        var clipsDirectory = Path.Combine(workDirectory, "tts-clips");
        Directory.CreateDirectory(clipsDirectory);
        var concatEntries = new List<string>();

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var rawPath = Path.Combine(clipsDirectory, $"{segment.Id:00000}-raw.wav");
            var slotPath = Path.Combine(clipsDirectory, $"{segment.Id:00000}-slot.wav");
            var nextStart = index + 1 < segments.Count
                ? segments[index + 1].StartMilliseconds / 1000d
                : videoDurationSeconds;
            var start = segment.StartMilliseconds / 1000d;
            var available = SegmentTiming.GetAvailableSeconds(segments, index, videoDurationSeconds);

            await SynthesizeToFitAsync(segment, voice, ollamaModel, rawPath, available, glossary, cancellationToken);
            var firstDelay = index == 0 ? start : 0;
            var clipDuration = index == 0 ? nextStart : Math.Max(0.01, nextStart - start);
            await RenderSlotAsync(rawPath, slotPath, segment.AppliedSpeedRatio, firstDelay, clipDuration, cancellationToken);
            segment.SynthesizedAudioPath = rawPath;
            concatEntries.Add(slotPath);
            logger.LogInformation("Voix {Current}/{Total}", index + 1, segments.Count);
        }

        var concatFile = Path.Combine(clipsDirectory, "concat.txt");
        await File.WriteAllLinesAsync(concatFile, concatEntries.Select(ToConcatEntry), cancellationToken);
        await processRunner.RunAsync(tools.Ffmpeg,
        [
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "concat", "-safe", "0", "-i", concatFile,
            "-af", $"apad=whole_dur={F(videoDurationSeconds)},atrim=duration={F(videoDurationSeconds)}",
            "-ar", settings.Audio.SampleRate.ToString(), "-ac", "2", "-c:a", PcmCodec(), outputWav
        ], cancellationToken: cancellationToken);
    }

    private async Task SynthesizeToFitAsync(
        DubSegment segment,
        VoiceProfile voice,
        string? ollamaModel,
        string rawPath,
        double availableSeconds,
        Glossary? glossary,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await synthesizer.SynthesizeAsync(segment.Translation, rawPath, voice, cancellationToken);
            var duration = await mediaProbe.GetAudioDurationAsync(rawPath, cancellationToken);
            segment.SynthesizedDurationSeconds = duration;
            var requiredSpeed = duration / availableSeconds;
            if (requiredSpeed <= settings.Tts.MaxSpeedRatio)
            {
                segment.AppliedSpeedRatio = Math.Max(1, requiredSpeed);
                return;
            }

            if (attempt < 2)
            {
                logger.LogInformation("Reformulation du segment {Id} pour respecter son créneau", segment.Id);
                segment.Translation = await translator.ShortenAsync(
                    segment.Translation, duration, availableSeconds * settings.Tts.MaxSpeedRatio, ollamaModel, cancellationToken, glossary);
            }
        }

        var finalDuration = segment.SynthesizedDurationSeconds;
        var fallbackSpeed = Math.Max(1, finalDuration / availableSeconds);
        if (fallbackSpeed > settings.Tts.AbsoluteMaxSpeedRatio)
        {
            throw new InvalidDataException(
                $"Segment {segment.Id} requires speed x{fallbackSpeed:0.00}, exceeding the limit " +
                $"x{settings.Tts.AbsoluteMaxSpeedRatio:0.00}. Shorten its translation in segments.json and retry; " +
                "rendering was stopped to avoid cutting spoken words.");
        }

        segment.AppliedSpeedRatio = fallbackSpeed;
        logger.LogWarning(
            "Segment {Id} encore trop long après deux reformulations : accélération exceptionnelle x{Speed:0.00} (limite habituelle x{Limit:0.00})",
            segment.Id,
            fallbackSpeed,
            settings.Tts.MaxSpeedRatio);
    }

    private Task RenderSlotAsync(
        string input,
        string output,
        double speedRatio,
        double initialDelaySeconds,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        var filters = new List<string> { $"aresample={settings.Audio.SampleRate}" };
        while (speedRatio > 2)
        {
            filters.Add("atempo=2");
            speedRatio /= 2;
        }
        if (speedRatio > 1.0001)
        {
            filters.Add($"atempo={F(speedRatio)}");
        }

        if (initialDelaySeconds > 0.0001)
        {
            var delay = (long)Math.Round(initialDelaySeconds * 1000);
            filters.Add($"adelay={delay}|{delay}");
        }

        filters.Add($"apad=whole_dur={F(durationSeconds)}");
        filters.Add($"atrim=duration={F(durationSeconds)}");
        return processRunner.RunAsync(tools.Ffmpeg,
        [
            "-hide_banner", "-loglevel", "error", "-y", "-i", input,
            "-af", string.Join(',', filters), "-ar", settings.Audio.SampleRate.ToString(), "-ac", "2",
            "-c:a", PcmCodec(), output
        ], cancellationToken: cancellationToken);
    }

    private string PcmCodec() => settings.Audio.BitDepth == 24 ? "pcm_s24le" : "pcm_s16le";
    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string ToConcatEntry(string path) => $"file '{Path.GetFullPath(path).Replace("'", "'\\''").Replace('\\', '/')}'";
}
