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
        SegmentTiming.Validate(segments, videoDurationSeconds);

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
        var bestText = segment.Translation;
        var bestDuration = double.PositiveInfinity;
        var candidatePath = Path.ChangeExtension(rawPath, $".{Guid.NewGuid():N}.candidate.wav");
        try
        {
            await TryCandidateAsync(bestText);
            if (bestDuration / availableSeconds > settings.Tts.AbsoluteMaxSpeedRatio)
            {
                logger.LogInformation("Nouvel essai vocal du segment {Id} sans changer son texte", segment.Id);
                await TryCandidateAsync(bestText);
            }

            for (var attempt = 0; attempt < 2 && bestDuration / availableSeconds > settings.Tts.MaxSpeedRatio; attempt++)
            {
                logger.LogInformation("Reformulation du segment {Id} pour respecter son créneau", segment.Id);
                var targetSeconds = availableSeconds * settings.Tts.MaxSpeedRatio;
                var shortened = await translator.ShortenAsync(
                    bestText, bestDuration, attempt == 0 ? targetSeconds : targetSeconds * 0.85,
                    ollamaModel, cancellationToken, glossary);
                if (shortened is not null)
                {
                    await TryCandidateAsync(shortened);
                }
            }
        }
        finally
        {
            File.Delete(candidatePath);
        }

        var speed = Math.Max(1, bestDuration / availableSeconds);
        if (speed > settings.Tts.AbsoluteMaxSpeedRatio)
        {
            throw new InvalidDataException(
                $"Segment {segment.Id} requires speed x{speed:0.00} for its shortest clip " +
                $"({bestDuration:0.00} s for {availableSeconds:0.00} s), exceeding the limit " +
                $"x{settings.Tts.AbsoluteMaxSpeedRatio:0.00}. Shorten its translation in segments.json and retry; " +
                "rendering was stopped to avoid cutting spoken words.");
        }

        segment.Translation = bestText;
        segment.SynthesizedDurationSeconds = bestDuration;
        segment.AppliedSpeedRatio = speed;
        if (speed > settings.Tts.MaxSpeedRatio)
        {
            logger.LogWarning(
                "Segment {Id} : accélération exceptionnelle x{Speed:0.00} (limite habituelle x{Limit:0.00})",
                segment.Id, speed, settings.Tts.MaxSpeedRatio);
        }

        async Task TryCandidateAsync(string text)
        {
            await synthesizer.SynthesizeAsync(text, candidatePath, voice, cancellationToken);
            var duration = await mediaProbe.GetAudioDurationAsync(candidatePath, cancellationToken);
            if (duration < bestDuration)
            {
                File.Move(candidatePath, rawPath, overwrite: true);
                bestText = text;
                bestDuration = duration;
            }
        }
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
