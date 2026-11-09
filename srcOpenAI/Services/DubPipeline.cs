using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Utils;
using Microsoft.Extensions.Logging;

namespace LocalDub.Services;

/// <summary>
/// Orchestrates the full French-to-English dubbing pipeline: audio extraction, optional vocal
/// separation, transcription, translation, speech synthesis, timeline assembly, subtitle
/// generation and final video/audio mixing. Intermediate artifacts are cached in a per-input
/// work directory so that interrupted runs can resume without repeating expensive steps.
/// </summary>
public sealed class DubPipeline(
    AppSettings settings,
    PathResolver paths,
    MediaProbe mediaProbe,
    AudioExtractor audioExtractor,
    SourceSeparator separator,
    WhisperTranscriber transcriber,
    InitialSpeechAlignment initialSpeech,
    TranscriptRepeatRepair repeatRepair,
    GlossaryService glossaries,
    OpenAiTranslator translator,
    VoiceProfileService voices,
    AudioTimelineBuilder timelineBuilder,
    SubtitleGenerator subtitleGenerator,
    VideoMixer videoMixer,
    ILogger<DubPipeline> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Runs the dubbing pipeline end to end for the given options and returns the paths of the
    /// produced artifacts (video, WAV, SRT, manifest, work directory).
    /// </summary>
    public async Task<DubArtifacts> RunAsync(DubOptions options, CancellationToken cancellationToken)
    {
        var input = Path.GetFullPath(options.InputPath);
        if (!File.Exists(input))
        {
            throw new FileNotFoundException("Source video was not found.", input);
        }

        var outputBase = Path.Combine(Path.GetDirectoryName(input)!, $"{Path.GetFileNameWithoutExtension(input)}-EN-OpenAI");
        var outputVideo = outputBase + Path.GetExtension(input);
        var outputWav = $"{outputBase}.wav";
        var outputSrt = $"{outputBase}.srt";
        var outputManifest = $"{outputBase}.json";
        var finalOutputs = options.ProductionMode == ProductionMode.CompleteVideo
            ? new[] { outputVideo, outputWav, outputSrt, outputManifest }
            : new[] { outputWav, outputSrt, outputManifest };
        if (finalOutputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != finalOutputs.Length)
        {
            throw new ArgumentException("This input extension conflicts with an export. Use --production wav.");
        }
        foreach (var output in finalOutputs)
        {
            OutputPathPolicy.EnsureWritableOutput(input, output, options.Overwrite);
        }

        var baseVoice = await voices.GetRequiredAsync(options.VoiceProfileId, cancellationToken);
        var voice = voices.ApplyOverrides(baseVoice, options.VoiceReferencePath, options.VoiceVariant);
        var workDirectory = GetWorkDirectory(input, options, voice);
        Directory.CreateDirectory(workDirectory);
        await using var workLock = new FileStream(Path.Combine(workDirectory, "run.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var media = await mediaProbe.ProbeAsync(input, cancellationToken);
        logger.LogInformation("Source video: {Duration:mm\\:ss}, audio {Rate} Hz", TimeSpan.FromSeconds(media.DurationSeconds), media.AudioSampleRate);

        var originalMix = Path.Combine(workDirectory, "original-48k.wav");
        if (!File.Exists(originalMix))
        {
            await audioExtractor.ExtractForMixingAsync(input, originalMix, cancellationToken);
        }

        string transcriptionSource = originalMix;
        string? accompaniment = null;
        if (options.AudioMode == AudioMode.Separate)
        {
            var separated = await separator.SeparateAsync(originalMix, Path.Combine(workDirectory, "separated"), cancellationToken);
            transcriptionSource = separated.VocalsPath;
            accompaniment = separated.AccompanimentPath;
        }

        var segmentsPath = Path.Combine(workDirectory, "segments.json");
        IReadOnlyList<DubSegment> segments;
        if (File.Exists(segmentsPath))
        {
            segments = await LoadSegmentsAsync(segmentsPath, cancellationToken);
            logger.LogInformation("Resuming from {Count} saved narration units", segments.Count);
        }
        else
        {
            var (cues, words) = await ReadSourceCuesAsync(options, transcriptionSource, workDirectory, cancellationToken);
            var normalized = SegmentTiming.RemoveRedundantZeroDurationSegments(cues);
            if (normalized.Count != cues.Count)
            {
                logger.LogWarning("Discarded duplicate zero-duration cues: {Count}", cues.Count - normalized.Count);
            }
            var repaired = await repeatRepair.RepairAsync(normalized, transcriptionSource, workDirectory, cancellationToken);
            var aligned = await initialSpeech.AlignAsync(repaired, transcriptionSource, media.DurationSeconds, cancellationToken);
            SegmentTiming.Validate(aligned, media.DurationSeconds);
            segments = NarrationSegmenter.Group(aligned, words);
            SegmentTiming.Validate(segments, media.DurationSeconds);
            await SaveSegmentsAsync(segmentsPath, segments, cancellationToken);
            logger.LogInformation("Grouped {Cues} source cues into {Units} narration units", aligned.Count, segments.Count);
        }
        SegmentTiming.Validate(segments, media.DurationSeconds);

        var glossary = await glossaries.LoadAsync(
            options.GlossaryName, options.PreservedTerms, cancellationToken);
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment.Translation)))
        {
            await translator.TranslateAsync(segments, glossary, options.Model, media.DurationSeconds, options.BaseSpeedRatio,
                () => SaveSegmentsAsync(segmentsPath, segments, cancellationToken), cancellationToken);
        }

        await timelineBuilder.BuildAsync(
            segments, voice, options.TimingModel ?? options.Model, media.DurationSeconds, options.BaseSpeedRatio,
            workDirectory, outputWav, cancellationToken, glossary);
        await SaveSegmentsAsync(segmentsPath, segments, cancellationToken);
        var captions = segments.Select((segment, index) => new DubSegment
        {
            Id = segment.Id,
            StartMilliseconds = segment.StartMilliseconds,
            EndMilliseconds = Math.Min(
                index + 1 < segments.Count ? segments[index + 1].StartMilliseconds : (long)Math.Ceiling(media.DurationSeconds * 1000),
                Math.Min(segment.EndMilliseconds, segment.StartMilliseconds +
                    Math.Max(1, (long)Math.Ceiling(segment.SynthesizedDurationSeconds / segment.AppliedSpeedRatio * 1000)))),
            Translation = segment.Translation
        }).ToArray();
        await subtitleGenerator.GenerateAsync(NarrationSubtitleLayout.Split(captions), outputSrt, cancellationToken);

        if (options.ProductionMode == ProductionMode.CompleteVideo)
        {
            var temporaryVideo = Path.Combine(
                Path.GetDirectoryName(outputVideo)!,
                $".{Path.GetFileNameWithoutExtension(outputVideo)}.processing{Path.GetExtension(outputVideo)}");
            try
            {
                await videoMixer.MixAsync(input, outputWav, originalMix, accompaniment, options.AudioMode, temporaryVideo, cancellationToken);
                File.Move(temporaryVideo, outputVideo, overwrite: options.Overwrite);
            }
            finally
            {
                if (File.Exists(temporaryVideo))
                {
                    File.Delete(temporaryVideo);
                }
            }
        }

        var manifest = new
        {
            source = input,
            outputVideo = options.ProductionMode == ProductionMode.CompleteVideo ? outputVideo : null,
            outputWav,
            outputSrt,
            language = new { source = "fr", target = "en-US" },
            audioMode = options.AudioMode.ToString(),
            productionMode = options.ProductionMode.ToString(),
            voice = new { profile = voice.Id, reference = voice.ReferenceAudio, variant = options.VoiceVariant },
            provider = "openai",
            translationModel = options.Model,
            timingModel = options.TimingModel ?? options.Model,
            segmentation = "narration-units",
            baseSpeedRatio = options.BaseSpeedRatio,
            transcript = options.TranscriptPath,
            preservedTerms = options.PreservedTerms,
            durationSeconds = media.DurationSeconds,
            segments = segments.Select((segment, index) => new
            {
                segment.Id,
                startMs = segment.StartMilliseconds,
                endMs = segment.EndMilliseconds,
                voiceEndMs = captions[index].EndMilliseconds,
                source = segment.SourceText,
                segment.SourceCueIds,
                segment.Translation,
                segment.SynthesizedDurationSeconds,
                segment.AppliedSpeedRatio
            })
        };
        await File.WriteAllTextAsync(outputManifest, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        var primaryOutput = options.ProductionMode == ProductionMode.CompleteVideo ? outputVideo : outputWav;
        logger.LogInformation("Dubbing complete: {Output}", primaryOutput);
        return new DubArtifacts(options.ProductionMode == ProductionMode.CompleteVideo ? outputVideo : null, outputWav, outputSrt, outputManifest, workDirectory);
    }

    private async Task<(IReadOnlyList<DubSegment> Cues, IReadOnlyList<TimedSourceWord> Words)> ReadSourceCuesAsync(
        DubOptions options, string audioSource, string workDirectory, CancellationToken cancellationToken)
    {
        IReadOnlyList<DubSegment>? imported = null;
        if (options.TranscriptPath is not null)
        {
            await using var stream = File.OpenRead(options.TranscriptPath);
            imported = await ReadBaselineAsync(stream, cancellationToken);
            if (!NarrationSegmenter.HasInternalSentenceBoundaries(imported))
            {
                return (imported, []);
            }
        }

        try
        {
            var asrWav = Path.Combine(workDirectory, "speech-16k.wav");
            if (!File.Exists(asrWav))
            {
                await audioExtractor.ExtractForTranscriptionAsync(audioSource, asrWav, cancellationToken);
            }
            var transcription = await transcriber.TranscribeAsync(asrWav, Path.Combine(workDirectory, "transcription-fr"), cancellationToken);
            return (imported ?? transcription.Cues, transcription.Words);
        }
        catch (Exception error) when (imported is not null && error is IOException or ProcessExecutionException)
        {
            logger.LogWarning("Word timing could not be obtained for the imported transcript. Using whole cue boundaries.");
            return (imported, []);
        }
    }

    private string GetWorkDirectory(string input, DubOptions options, VoiceProfile voice)
    {
        var file = new FileInfo(input);
        var preservedTerms = string.Join(
            '|',
            options.PreservedTerms.OrderBy(term => term, StringComparer.OrdinalIgnoreCase));
        var pipelineSettings = JsonSerializer.Serialize(new
        {
            version = 2,
            settings.Whisper,
            settings.Separation,
            settings.OpenAI,
            options.Model,
            options.TimingModel,
            transcriptSignature = GetFileContentSignature(options.TranscriptPath),
            settings.Tts,
            settings.Audio,
            voice,
            referenceSignature = GetFileContentSignature(voices.ResolveReference(voice)),
            options.VoiceProfileId,
            options.VoiceReferencePath,
            options.VoiceVariant
        }, JsonOptions);
        var signature = $"{pipelineSettings}|{input}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{options.AudioMode}|{options.GlossaryName}|glossary:{GetGlossaryContentSignature(options.GlossaryName)}";
        if (!string.IsNullOrEmpty(preservedTerms))
        {
            signature += $"|preserve:{preservedTerms}";
        }
        if (options.BaseSpeedRatio != 1)
        {
            signature += $"|base-speed:{options.BaseSpeedRatio.ToString("R", CultureInfo.InvariantCulture)}";
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature))).ToLowerInvariant()[..10];
        var name = string.Concat(Path.GetFileNameWithoutExtension(input).Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        return Path.Combine(paths.Resolve(settings.Paths.OutputRoot), "work", $"{name}-{hash}");
    }

    private static string GetFileContentSignature(string? path)
    {
        if (path is null) return "none";
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>
    /// Computes a short hash of the glossary file content so that editing an existing glossary
    /// (without renaming it) invalidates the cached transcription/translation work directory,
    /// instead of silently reusing stale translations from a previous run.
    /// </summary>
    private string GetGlossaryContentSignature(string? glossaryName)
    {
        if (string.IsNullOrWhiteSpace(glossaryName))
        {
            return "none";
        }

        var file = glossaryName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? glossaryName : $"{glossaryName}.json";
        var path = Path.IsPathRooted(file) ? file : Path.Combine(paths.Resolve(settings.Paths.GlossariesRoot), file);
        if (!File.Exists(path))
        {
            return "missing";
        }

        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()[..10];
    }

    private static async Task<IReadOnlyList<DubSegment>> LoadSegmentsAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<DubSegment>>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidDataException($"Invalid segment file: {path}");
    }

    /// <summary>Imports only the source text and timing from an existing LocalDub result.</summary>
    internal static async Task<IReadOnlyList<DubSegment>> ReadBaselineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var payload = document.RootElement;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("segments", out var segments))
        {
            payload = segments;
        }
        var source = payload.ValueKind == JsonValueKind.Array ? payload.Deserialize<List<DubSegment>>(JsonOptions) : null;
        if (source is null || source.Count == 0 || source.Any(segment => segment is null ||
            segment.Id <= 0 || string.IsNullOrWhiteSpace(segment.SourceText)) ||
            source.Select(segment => segment.Id).Distinct().Count() != source.Count)
        {
            throw new InvalidDataException("The baseline must contain nonempty source segments with unique positive IDs.");
        }
        foreach (var segment in source)
        {
            segment.Translation = string.Empty;
        }
        return source;
    }

    private static async Task SaveSegmentsAsync(string path, IReadOnlyList<DubSegment> segments, CancellationToken cancellationToken)
    {
        var temporary = $"{path}.tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, segments, JsonOptions, cancellationToken);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
