using System.Globalization;
using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Utils;

namespace LocalDub.Services;

/// <summary>
/// Mixes the dubbed voice track back into the source video using FFmpeg, according to the selected
/// <see cref="AudioMode"/> (fully separated background, ducked original mix, or external mix only).
/// </summary>
public sealed class VideoMixer(ToolPaths tools, ProcessRunner processRunner, AppSettings settings)
{
    /// <summary>
    /// Produces <paramref name="outputVideo"/> by combining the original video track with the
    /// dubbed audio, mixed according to <paramref name="mode"/>.
    /// </summary>
    public Task MixAsync(
        string inputVideo,
        string dubbedAudio,
        string originalMix,
        string? accompaniment,
        AudioMode mode,
        string outputVideo,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dubbedAudio) || !File.Exists(dubbedAudio))
        {
            throw new FileNotFoundException("La piste de doublage est requise pour le mixage vidéo.", dubbedAudio);
        }

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y", "-i", inputVideo
        };

        string filter;
        if (mode == AudioMode.Separate)
        {
            if (string.IsNullOrWhiteSpace(accompaniment))
            {
                throw new ArgumentException("La piste d'accompagnement est requise en mode separate.", nameof(accompaniment));
            }

            args.AddRange(["-i", accompaniment, "-i", dubbedAudio]);
            filter = "[1:a][2:a]amix=inputs=2:duration=longest:normalize=0,alimiter=limit=0.95[outa]";
        }
        else if (mode == AudioMode.Duck)
        {
            args.AddRange(["-i", originalMix, "-i", dubbedAudio]);
            var threshold = Math.Pow(10, settings.Audio.DuckLevelDb / 20d).ToString("0.######", CultureInfo.InvariantCulture);
            filter = $"[1:a][2:a]sidechaincompress=threshold={threshold}:ratio=20:attack={settings.Audio.DuckingFadeMilliseconds}:release=350[ducked];" +
                     "[ducked][2:a]amix=inputs=2:duration=longest:normalize=0,alimiter=limit=0.95[outa]";
        }
        else
        {
            args.AddRange(["-i", dubbedAudio]);
            filter = "[1:a]anull[outa]";
        }

        args.AddRange([
            "-filter_complex", filter,
            "-map", "0:v:0", "-map", "[outa]",
            "-c:v", "copy", "-c:a", settings.Audio.OutputCodec,
            "-b:a", settings.Audio.OutputBitrate,
            "-shortest", outputVideo
        ]);
        return processRunner.RunAsync(tools.Ffmpeg, args, cancellationToken: cancellationToken);
    }
}
