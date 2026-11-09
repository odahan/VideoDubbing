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
    OpenAiTranslator translator,
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
        string model,
        double videoDurationSeconds,
        double baseSpeedRatio,
        string workDirectory,
        string outputWav,
        CancellationToken cancellationToken,
        Glossary? glossary = null)
    {
        SegmentTiming.Validate(segments, videoDurationSeconds);
        var pace = new NarrationPace(baseSpeedRatio, settings.Tts.MaxSpeedRatio, settings.Tts.AbsoluteMaxSpeedRatio);

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
            var available = NarrationSegmenter.GetSpeechBudget(segments, index, videoDurationSeconds);
            var previous = index > 0 ? segments[index - 1] : null;
            var previousWording = previous is not null && segment.StartMilliseconds - previous.EndMilliseconds <= 500 &&
                !OpenAiTranslator.HasSameWording(segment.SourceText, previous.SourceText)
                ? previous.Translation
                : null;

            await SynthesizeToFitAsync(segment, voice, model, rawPath, available, pace, glossary, previousWording, cancellationToken);
            var firstDelay = index == 0 ? start : 0;
            var clipDuration = index == 0 ? nextStart : Math.Max(0.01, nextStart - start);
            await RenderSlotAsync(rawPath, slotPath, segment.AppliedSpeedRatio, firstDelay, clipDuration, cancellationToken);
            segment.SynthesizedAudioPath = rawPath;
            concatEntries.Add(slotPath);
            logger.LogInformation("Voice {Current}/{Total}", index + 1, segments.Count);
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
        string model,
        string rawPath,
        double availableSeconds,
        NarrationPace pace,
        Glossary? glossary,
        string? previousWording,
        CancellationToken cancellationToken)
    {
        var bestText = segment.Translation;
        if (previousWording is not null && OpenAiTranslator.HasSameWording(bestText, previousWording))
        {
            bestText = await translator.VaryAdjacentWordingAsync(segment.SourceText, bestText, previousWording, model, glossary, cancellationToken);
        }
        var bestDuration = double.PositiveInfinity;
        var candidatePath = Path.ChangeExtension(rawPath, $".{Guid.NewGuid():N}.candidate.wav");
        try
        {
            await TryCandidateAsync(bestText, retryOverHardLimit: true);

            for (var attempt = 0; attempt < 2 && pace.RequiredRatio(bestDuration, availableSeconds) > pace.PreferredRatio; attempt++)
            {
                logger.LogInformation("Shortening segment {Id} to fit its slot", segment.Id);
                var targetSeconds = availableSeconds * pace.PreferredRatio;
                var shortened = await translator.ShortenAsync(
                    segment.SourceText, bestText, bestDuration, attempt == 0 ? targetSeconds : targetSeconds * 0.85,
                    model, cancellationToken, glossary, previousWording);
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

        var speed = pace.RequiredRatio(bestDuration, availableSeconds);
        if (speed > pace.AbsoluteMaximum)
        {
            throw new InvalidDataException(
                $"Segment {segment.Id} requires speed x{speed:0.00} for its shortest clip " +
                $"({bestDuration:0.00} s for {availableSeconds:0.00} s), exceeding the limit " +
                $"x{pace.AbsoluteMaximum:0.00}. Shorten its translation in segments.json and retry; " +
                "rendering was stopped to avoid cutting spoken words.");
        }

        segment.Translation = bestText;
        segment.SynthesizedDurationSeconds = bestDuration;
        segment.AppliedSpeedRatio = speed;
        if (speed > pace.PreferredRatio)
        {
            logger.LogWarning(
                "Segment {Id} needed x{Speed:0.00} playback speed (preferred limit x{Limit:0.00})",
                segment.Id, speed, pace.PreferredRatio);
        }

        async Task TryCandidateAsync(string text, bool retryOverHardLimit = false)
        {
            var spoken = SpokenEnglish.ForSynthesis(text);
            var retryThreshold = text.Trim().Any(char.IsWhiteSpace)
                ? (retryOverHardLimit ? pace.AbsoluteMaximum : double.PositiveInfinity)
                : pace.PreferredRatio;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await synthesizer.SynthesizeAsync(spoken, candidatePath, voice, cancellationToken);
                var duration = await mediaProbe.GetAudioDurationAsync(candidatePath, cancellationToken);
                if (duration < bestDuration)
                {
                    File.Move(candidatePath, rawPath, overwrite: true);
                    bestText = text;
                    bestDuration = duration;
                }
                if (attempt == 1 || pace.RequiredRatio(bestDuration, availableSeconds) <= retryThreshold)
                {
                    break;
                }
                logger.LogInformation("Retrying the voice for segment {Id} with the same text", segment.Id);
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
            filters.Add($"adelay={delay}:all=1");
            filters.Add("asetpts=N/SR/TB");
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
