using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Utils;
using Microsoft.Extensions.Logging;

namespace LocalDub.Services;

public sealed class DubPipeline(
    AppSettings settings,
    PathResolver paths,
    MediaProbe mediaProbe,
    AudioExtractor audioExtractor,
    SourceSeparator separator,
    WhisperTranscriber transcriber,
    GlossaryService glossaries,
    OllamaTranslator translator,
    VoiceProfileService voices,
    AudioTimelineBuilder timelineBuilder,
    SubtitleGenerator subtitleGenerator,
    VideoMixer videoMixer,
    ProcessRunner processRunner,
    ILogger<DubPipeline> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<DubArtifacts> RunAsync(DubOptions options, CancellationToken cancellationToken)
    {
        var input = Path.GetFullPath(options.InputPath);
        if (!File.Exists(input))
        {
            throw new FileNotFoundException("Vidéo source introuvable.", input);
        }

        var outputVideo = OutputPathPolicy.GetEnglishVideoPath(input);
        var outputBase = Path.Combine(Path.GetDirectoryName(outputVideo)!, Path.GetFileNameWithoutExtension(outputVideo));
        var outputWav = $"{outputBase}.wav";
        var outputSrt = $"{outputBase}.srt";
        var outputManifest = $"{outputBase}.json";
        var finalOutputs = options.ProductionMode == ProductionMode.CompleteVideo
            ? new[] { outputVideo, outputWav, outputSrt, outputManifest }
            : new[] { outputWav, outputSrt, outputManifest };
        foreach (var output in finalOutputs)
        {
            OutputPathPolicy.EnsureWritableOutput(input, output, options.Overwrite);
        }

        var workDirectory = GetWorkDirectory(input, options);
        Directory.CreateDirectory(workDirectory);
        var media = await mediaProbe.ProbeAsync(input, cancellationToken);
        logger.LogInformation("Vidéo source : {Duration:mm\\:ss}, audio {Rate} Hz", TimeSpan.FromSeconds(media.DurationSeconds), media.AudioSampleRate);

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

        var asrWav = Path.Combine(workDirectory, "speech-16k.wav");
        if (!File.Exists(asrWav))
        {
            await audioExtractor.ExtractForTranscriptionAsync(transcriptionSource, asrWav, cancellationToken);
        }

        var segmentsPath = Path.Combine(workDirectory, "segments.json");
        IReadOnlyList<DubSegment> segments;
        if (File.Exists(segmentsPath))
        {
            segments = await LoadSegmentsAsync(segmentsPath, cancellationToken);
            logger.LogInformation("Reprise de {Count} segments existants", segments.Count);
        }
        else
        {
            var transcription = await transcriber.TranscribeAsync(asrWav, Path.Combine(workDirectory, "transcription-fr"), cancellationToken);
            segments = transcription.Segments;
            await SaveSegmentsAsync(segmentsPath, segments, cancellationToken);
        }

        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment.Translation)))
        {
            var glossary = await glossaries.LoadAsync(
                options.GlossaryName,
                options.PreservedTerms,
                cancellationToken);
            await translator.TranslateAsync(segments, glossary, options.OllamaModel, cancellationToken);
            await SaveSegmentsAsync(segmentsPath, segments, cancellationToken);
        }

        if (settings.Ollama.UnloadBeforeTts)
        {
            await StopOllamaModelAsync(options.OllamaModel ?? settings.Ollama.Model, cancellationToken);
        }
        await subtitleGenerator.GenerateAsync(segments, outputSrt, cancellationToken);
        var baseVoice = await voices.GetRequiredAsync(options.VoiceProfileId, cancellationToken);
        var voice = voices.ApplyOverrides(baseVoice, options.VoiceReferencePath, options.VoiceVariant);
        await timelineBuilder.BuildAsync(
            segments, voice, options.OllamaModel, media.DurationSeconds, workDirectory, outputWav, cancellationToken);
        await SaveSegmentsAsync(segmentsPath, segments, cancellationToken);

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
            ollamaModel = options.OllamaModel ?? settings.Ollama.Model,
            timingModel = settings.Ollama.TimingModel,
            preservedTerms = options.PreservedTerms,
            durationSeconds = media.DurationSeconds,
            segments
        };
        await File.WriteAllTextAsync(outputManifest, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        var primaryOutput = options.ProductionMode == ProductionMode.CompleteVideo ? outputVideo : outputWav;
        logger.LogInformation("Doublage terminé : {Output}", primaryOutput);
        return new DubArtifacts(options.ProductionMode == ProductionMode.CompleteVideo ? outputVideo : null, outputWav, outputSrt, outputManifest, workDirectory);
    }

    private string GetWorkDirectory(string input, DubOptions options)
    {
        var file = new FileInfo(input);
        var preservedTerms = string.Join(
            '|',
            options.PreservedTerms.OrderBy(term => term, StringComparer.OrdinalIgnoreCase));
        var signature = $"{input}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{options.AudioMode}|{options.OllamaModel ?? settings.Ollama.Model}|{options.GlossaryName}";
        if (!string.IsNullOrEmpty(preservedTerms))
        {
            signature += $"|preserve:{preservedTerms}";
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature))).ToLowerInvariant()[..10];
        var name = string.Concat(Path.GetFileNameWithoutExtension(input).Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        return Path.Combine(paths.Resolve(settings.Paths.OutputRoot), "work", $"{name}-{hash}");
    }

    private async Task StopOllamaModelAsync(string model, CancellationToken cancellationToken)
    {
        try
        {
            await processRunner.RunAsync("ollama", ["stop", model], cancellationToken: cancellationToken);
            logger.LogDebug("Modèle Ollama déchargé avant les étapes CUDA audio");
        }
        catch (Exception exception) when (exception is ProcessExecutionException or FileNotFoundException)
        {
            logger.LogWarning("Impossible de décharger automatiquement le modèle Ollama : {Message}", exception.Message);
        }
    }

    private static async Task<IReadOnlyList<DubSegment>> LoadSegmentsAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<DubSegment>>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidDataException($"Fichier de segments invalide : {path}");
    }

    private static async Task SaveSegmentsAsync(string path, IReadOnlyList<DubSegment> segments, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, segments, JsonOptions, cancellationToken);
    }
}
