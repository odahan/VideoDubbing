using System.Text.Json.Serialization;

namespace LocalDub.Models;

/// <summary>Mandatory terminology and preserved terms applied by the translator for a given topic.</summary>
public sealed class Glossary
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("terms")]
    public Dictionary<string, string> Terms { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("preserve")]
    public List<string> Preserve { get; init; } = [];
}
