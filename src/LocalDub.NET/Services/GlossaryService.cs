using System.Text.Json;
using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Utils;

namespace LocalDub.Services;

public sealed class GlossaryService(AppSettings settings, PathResolver paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Glossary?> LoadAsync(
        string? name,
        IReadOnlyList<string>? additionalPreservedTerms,
        CancellationToken cancellationToken)
    {
        Glossary? glossary = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            if (additionalPreservedTerms is null || additionalPreservedTerms.Count == 0)
            {
                return null;
            }
        }
        else
        {
            var file = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.json";
            var path = Path.IsPathRooted(file) ? file : Path.Combine(paths.Resolve(settings.Paths.GlossariesRoot), file);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Glossaire introuvable : {path}", path);
            }

            await using var stream = File.OpenRead(path);
            glossary = await JsonSerializer.DeserializeAsync<Glossary>(stream, JsonOptions, cancellationToken)
                       ?? throw new InvalidDataException($"Glossaire invalide : {path}");
        }

        var preserved = (glossary?.Preserve ?? [])
            .Concat(additionalPreservedTerms ?? [])
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new Glossary
        {
            Name = glossary?.Name ?? "Termes personnalisés",
            Terms = glossary?.Terms ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Preserve = preserved
        };
    }
}
