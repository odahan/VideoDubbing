using System.Text.Json;
using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Utils;

namespace LocalDub.Voices;

public sealed class VoiceProfileCatalog(AppSettings settings, PathResolver paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string ProfilesPath => paths.Resolve(settings.Paths.VoicesFile);
    public string VoicesDirectory => Path.GetDirectoryName(ProfilesPath)
        ?? throw new InvalidOperationException("Le dossier des voix est introuvable.");

    public async Task<VoiceProfilesDocument> LoadAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(ProfilesPath);
        return await JsonSerializer.DeserializeAsync<VoiceProfilesDocument>(stream, JsonOptions, cancellationToken)
            ?? new VoiceProfilesDocument();
    }

    public async Task SaveAsync(VoiceProfilesDocument document, CancellationToken cancellationToken)
    {
        CreateBackup();
        var temporaryPath = $"{ProfilesPath}.tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(document, JsonOptions), cancellationToken);
        File.Move(temporaryPath, ProfilesPath, overwrite: true);
    }

    private void CreateBackup()
    {
        if (!File.Exists(ProfilesPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(ProfilesPath)
            ?? throw new InvalidOperationException("Le dossier des profils est introuvable.");
        var name = Path.GetFileNameWithoutExtension(ProfilesPath);
        var extension = Path.GetExtension(ProfilesPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var backupPath = Path.Combine(directory, $"{name}.backup-{timestamp}{extension}");
        File.Copy(ProfilesPath, backupPath);
    }

    public string CopyReferenceAudio(string sourcePath, string profileId)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Fichier WAV introuvable.", source);
        }
        if (!Path.GetExtension(source).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("La référence doit être un fichier WAV.");
        }

        Directory.CreateDirectory(VoicesDirectory);
        var target = Path.Combine(VoicesDirectory, $"{profileId}.wav");
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return $"voices/{Path.GetFileName(target)}";
        }
        if (File.Exists(target))
        {
            throw new IOException($"Un fichier existe déjà : {target}");
        }

        File.Copy(source, target);
        return $"voices/{Path.GetFileName(target)}";
    }
}
