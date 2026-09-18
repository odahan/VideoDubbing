using System.Text.Json;
using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Utils;

namespace LocalDub.Services;

/// <summary>
/// Loads voice profiles from the configured voices catalog and applies runtime overrides such as a
/// custom reference audio clip or a named tuning variant.
/// </summary>
public sealed class VoiceProfileService(AppSettings settings, PathResolver paths)
{
    /// <summary>
    /// Names of the standard voice tuning variants supported by <see cref="GetVariantTuning"/>,
    /// in a stable order suitable for iteration (e.g. when generating audition previews).
    /// </summary>
    public static readonly IReadOnlyList<string> StandardVariants = ["neutral", "stable", "expressive"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>Loads every voice profile declared in the voices catalog file.</summary>
    public async Task<IReadOnlyList<VoiceProfile>> GetProfilesAsync(CancellationToken cancellationToken)
    {
        var file = paths.Resolve(settings.Paths.VoicesFile);
        await using var stream = File.OpenRead(file);
        var document = await JsonSerializer.DeserializeAsync<VoiceProfilesDocument>(stream, JsonOptions, cancellationToken);
        return document?.Profiles ?? [];
    }

    /// <summary>Lists the reference WAV files available next to the voices catalog file.</summary>
    public IReadOnlyList<string> GetReferenceAudioFiles()
    {
        var voicesDirectory = Path.GetDirectoryName(paths.Resolve(settings.Paths.VoicesFile));
        if (string.IsNullOrWhiteSpace(voicesDirectory) || !Directory.Exists(voicesDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(voicesDirectory, "*.wav", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Returns the voice profile with the given id, or throws <see cref="KeyNotFoundException"/> if it does not exist.</summary>
    public async Task<VoiceProfile> GetRequiredAsync(string id, CancellationToken cancellationToken)
    {
        var profiles = await GetProfilesAsync(cancellationToken);
        return profiles.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException($"Profil vocal inconnu : {id}");
    }

    /// <summary>Resolves a voice profile's reference audio clip to an absolute path, or null if it has none.</summary>
    public string? ResolveReference(VoiceProfile profile)
    {
        return string.IsNullOrWhiteSpace(profile.ReferenceAudio) ? null : paths.Resolve(profile.ReferenceAudio);
    }

    /// <summary>
    /// Returns a copy of <paramref name="profile"/> with an optional reference audio override and
    /// the tuning for the named <paramref name="variant"/> applied.
    /// </summary>
    public VoiceProfile ApplyOverrides(VoiceProfile profile, string? referencePath, string variant)
    {
        var reference = string.IsNullOrWhiteSpace(referencePath) ? profile.ReferenceAudio : Path.GetFullPath(referencePath);
        if (!string.IsNullOrWhiteSpace(reference) && !File.Exists(paths.Resolve(reference)))
        {
            throw new FileNotFoundException("Référence vocale introuvable.", paths.Resolve(reference));
        }

        var tuning = GetVariantTuning(variant, profile);
        return new VoiceProfile
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            Engine = profile.Engine,
            ReferenceAudio = reference,
            Temperature = tuning.Temperature,
            TopP = tuning.TopP,
            RepetitionPenalty = tuning.RepetitionPenalty,
            TopK = profile.TopK,
            Description = profile.Description
        };
    }

    /// <summary>
    /// Resolves the synthesis tuning (temperature, top-p, repetition penalty) for a named voice
    /// variant. "neutral" preserves the base profile's own tuning; "stable" and "expressive" are
    /// fixed presets shared by both interactive dubbing (<see cref="ApplyOverrides"/>) and voice
    /// audition previews (<see cref="ChatterboxSynthesizer.AuditionAsync"/>), so the two code paths
    /// cannot drift apart.
    /// </summary>
    public static (double Temperature, double TopP, double RepetitionPenalty) GetVariantTuning(string variant, VoiceProfile baseProfile) =>
        variant.ToLowerInvariant() switch
        {
            "neutral" => (baseProfile.Temperature, baseProfile.TopP, baseProfile.RepetitionPenalty),
            "stable" => (0.65, 0.90, 1.25),
            "expressive" => (0.95, 0.98, 1.10),
            _ => throw new ArgumentException("La variante vocale doit valoir neutral, stable ou expressive.", nameof(variant))
        };
}
