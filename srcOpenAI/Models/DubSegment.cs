using System.Text.Json.Serialization;

namespace LocalDub.Models;

/// <summary>A timed source cue or a complete unit of dubbed narration.</summary>
public sealed class DubSegment
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("startMs")]
    public long StartMilliseconds { get; init; }

    [JsonPropertyName("endMs")]
    public long EndMilliseconds { get; init; }

    [JsonPropertyName("source")]
    public string SourceText { get; init; } = string.Empty;

    [JsonPropertyName("translation")]
    public string Translation { get; set; } = string.Empty;

    [JsonPropertyName("sourceCueIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? SourceCueIds { get; init; }

    [JsonIgnore]
    public string? SynthesizedAudioPath { get; set; }

    [JsonIgnore]
    public double SynthesizedDurationSeconds { get; set; }

    [JsonIgnore]
    public double AppliedSpeedRatio { get; set; } = 1;
}
