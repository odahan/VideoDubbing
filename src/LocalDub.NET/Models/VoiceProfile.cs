using System.Text.Json.Serialization;

namespace LocalDub.Models;

public sealed class VoiceProfilesDocument
{
    [JsonPropertyName("profiles")]
    public List<VoiceProfile> Profiles { get; init; } = [];
}

public sealed class VoiceProfile
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("engine")]
    public string Engine { get; init; } = "chatterbox-turbo";

    [JsonPropertyName("referenceAudio")]
    public string? ReferenceAudio { get; init; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = 0.8;

    [JsonPropertyName("repetitionPenalty")]
    public double RepetitionPenalty { get; init; } = 1.2;

    [JsonPropertyName("topP")]
    public double TopP { get; init; } = 0.95;

    [JsonPropertyName("topK")]
    public int TopK { get; init; } = 1000;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}
