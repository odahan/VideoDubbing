using System.Globalization;
using System.Text.Json;
using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Utils;
using Microsoft.Extensions.Logging;

namespace LocalDub.Services;

/// <summary>Produces French SRT cues and the word timings used to find sentence starts within them.</summary>
public sealed class WhisperTranscriber(
    ToolPaths tools, ProcessRunner processes, AppSettings settings, ILogger<WhisperTranscriber> logger)
{
    /// <summary>Transcribes the audio locally and returns cue text and word timing independently.</summary>
    internal async Task<(IReadOnlyList<DubSegment> Cues, IReadOnlyList<TimedSourceWord> Words)> TranscribeAsync(
        string audioPath, string outputPrefix, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reading French cues and word timings with Whisper {Model}", settings.Whisper.ModelName);
        await processes.RunAsync(tools.Whisper,
        [
            "-m", tools.WhisperModel, "-f", audioPath, "-l", settings.Whisper.Language,
            "-t", settings.Whisper.Threads.ToString(CultureInfo.InvariantCulture),
            "-ml", settings.Whisper.MaxSegmentCharacters.ToString(CultureInfo.InvariantCulture),
            "-sow", "-osrt", "-ojf", "-of", outputPrefix
        ], cancellationToken: cancellationToken);

        var cues = SrtParser.Parse(await File.ReadAllTextAsync(outputPrefix + ".srt", cancellationToken));
        try
        {
            await using var json = File.OpenRead(outputPrefix + ".json");
            using var document = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken);
            return (cues, TimedSourceWord.Read(document.RootElement));
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            logger.LogWarning("Whisper word timings could not be read. Using whole cue boundaries.");
            return (cues, []);
        }
    }
}
