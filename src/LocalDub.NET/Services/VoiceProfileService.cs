using System.Text.Json;
using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Utils;

namespace LocalDub.Services;

public sealed class VoiceProfileService(AppSettings settings, PathResolver paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<IReadOnlyList<VoiceProfile>> GetProfilesAsync(CancellationToken cancellationToken)
    {
        var file = paths.Resolve(settings.Paths.VoicesFile);
        await using var stream = File.OpenRead(file);
        var document = await JsonSerializer.DeserializeAsync<VoiceProfilesDocument>(stream, JsonOptions, cancellationToken);
        return document?.Profiles ?? [];
    }

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

    public async Task<VoiceProfile> GetRequiredAsync(string id, CancellationToken cancellationToken)
    {
        var profiles = await GetProfilesAsync(cancellationToken);
        return profiles.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException($"Profil vocal inconnu : {id}");
    }

    public string? ResolveReference(VoiceProfile profile)
    {
        return string.IsNullOrWhiteSpace(profile.ReferenceAudio) ? null : paths.Resolve(profile.ReferenceAudio);
    }

    public VoiceProfile ApplyOverrides(VoiceProfile profile, string? referencePath, string variant)
    {
        var reference = string.IsNullOrWhiteSpace(referencePath) ? profile.ReferenceAudio : Path.GetFullPath(referencePath);
        if (!string.IsNullOrWhiteSpace(reference) && !File.Exists(paths.Resolve(reference)))
        {
            throw new FileNotFoundException("Référence vocale introuvable.", paths.Resolve(reference));
        }

        var tuning = variant.ToLowerInvariant() switch
        {
            "neutral" => (profile.Temperature, profile.TopP, profile.RepetitionPenalty),
            "stable" => (0.65, 0.90, 1.25),
            "expressive" => (0.95, 0.98, 1.10),
            _ => throw new ArgumentException("La variante vocale doit valoir neutral, stable ou expressive.", nameof(variant))
        };

        return new VoiceProfile
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            Engine = profile.Engine,
            ReferenceAudio = reference,
            Temperature = tuning.Item1,
            TopP = tuning.Item2,
            RepetitionPenalty = tuning.Item3,
            TopK = profile.TopK,
            Description = profile.Description
        };
    }
}
