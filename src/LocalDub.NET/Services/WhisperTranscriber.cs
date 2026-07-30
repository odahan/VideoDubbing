using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Utils;
using Microsoft.Extensions.Logging;

namespace LocalDub.Services;

public sealed class WhisperTranscriber(
    ToolPaths tools,
    ProcessRunner processRunner,
    AppSettings settings,
    ILogger<WhisperTranscriber> logger)
{
    public async Task<TranscriptionResult> TranscribeAsync(string audioPath, string outputPrefix, CancellationToken cancellationToken)
    {
        logger.LogInformation("Transcription française avec Whisper {Model}", settings.Whisper.ModelName);
        await processRunner.RunAsync(tools.Whisper,
        [
            "-m", tools.WhisperModel,
            "-f", audioPath,
            "-l", settings.Whisper.Language,
            "-t", settings.Whisper.Threads.ToString(),
            "-osrt",
            "-of", outputPrefix
        ], cancellationToken: cancellationToken);

        var srtPath = $"{outputPrefix}.srt";
        if (!File.Exists(srtPath))
        {
            throw new InvalidDataException("Whisper n'a pas produit le fichier SRT attendu.");
        }

        var content = await File.ReadAllTextAsync(srtPath, cancellationToken);
        var segments = SrtParser.Parse(content);
        if (segments.Count == 0)
        {
            throw new InvalidDataException("La transcription ne contient aucun segment exploitable.");
        }

        return new TranscriptionResult(string.Join(' ', segments.Select(x => x.SourceText)), segments, srtPath);
    }
}
