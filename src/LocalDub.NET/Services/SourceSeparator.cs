using LocalDub.Configuration;
using LocalDub.Utils;
using Microsoft.Extensions.Logging;

namespace LocalDub.Services;

/// <summary>Paths of the vocals and accompaniment stems produced by source separation.</summary>
public sealed record SeparatedAudio(string VocalsPath, string AccompanimentPath);

/// <summary>
/// Separates a mixed audio track into vocals and accompaniment stems using Demucs, so that the
/// original background music/effects can be preserved and remixed with the dubbed voice.
/// </summary>
public sealed class SourceSeparator(
    ToolPaths tools,
    ProcessRunner processRunner,
    AppSettings settings,
    ILogger<SourceSeparator> logger)
{
    /// <summary>
    /// Separates <paramref name="sourceWav"/> into vocals/accompaniment stems, reusing a previous
    /// result under <paramref name="outputRoot"/> if one already exists.
    /// </summary>
    public async Task<SeparatedAudio> SeparateAsync(string sourceWav, string outputRoot, CancellationToken cancellationToken)
    {
        if (!File.Exists(tools.SeparationPython))
        {
            throw new FileNotFoundException("L'environnement de séparation est absent. Exécutez 'localdub setup'.", tools.SeparationPython);
        }

        var trackName = Path.GetFileNameWithoutExtension(sourceWav);
        var resultDirectory = Path.Combine(outputRoot, settings.Separation.Model, trackName);
        var vocals = Path.Combine(resultDirectory, "vocals.wav");
        var accompaniment = Path.Combine(resultDirectory, "no_vocals.wav");
        if (File.Exists(vocals) && File.Exists(accompaniment))
        {
            logger.LogInformation("Reprise des stems déjà séparés");
            return new SeparatedAudio(vocals, accompaniment);
        }

        logger.LogInformation("Séparation de la voix et de l'accompagnement avec {Model}", settings.Separation.Model);
        var ffmpegDirectory = Path.GetDirectoryName(tools.Ffmpeg)
            ?? throw new InvalidOperationException("Le dossier de FFmpeg est introuvable.");
        var processPath = string.Join(
            Path.PathSeparator,
            new[] { ffmpegDirectory, Environment.GetEnvironmentVariable("PATH") }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        await processRunner.RunAsync(tools.SeparationPython,
        [
            "-m", "demucs.separate",
            "--two-stems", "vocals",
            "-n", settings.Separation.Model,
            "-d", settings.Separation.Device,
            "--out", outputRoot,
            sourceWav
        ], environment: new Dictionary<string, string?>
        {
            ["PATH"] = processPath
        }, cancellationToken: cancellationToken);

        if (!File.Exists(vocals) || !File.Exists(accompaniment))
        {
            throw new InvalidDataException($"Demucs n'a pas produit les stems attendus dans {resultDirectory}.");
        }

        return new SeparatedAudio(vocals, accompaniment);
    }
}
