using System.Text.Json.Serialization;

namespace LocalDub.Models;

/// <summary>Root document deserialized from the voices catalog file (voices/profiles.json).</summary>
public sealed class VoiceProfilesDocument
{
    [JsonPropertyName("profiles")]
    public List<VoiceProfile> Profiles { get; init; } = [];
}

/// <summary>A named text-to-speech voice configuration, including its default synthesis tuning.</summary>
public sealed class VoiceProfile
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("engine")]
    public string Engine { get; init; } = "chatterbox-turbo";

    /// <summary>Optional language override; null uses the application TTS language.</summary>
    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("exaggeration")]
    public double Exaggeration { get; init; } = 0.5;

    [JsonPropertyName("cfgWeight")]
    public double CfgWeight { get; init; } = 0.5;

    [JsonPropertyName("minP")]
    public double MinP { get; init; } = 0.05;

    [JsonPropertyName("outputGain")]
    public double OutputGain { get; init; } = 1.0;

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
