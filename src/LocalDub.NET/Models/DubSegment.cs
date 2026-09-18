using System.Text.Json.Serialization;

namespace LocalDub.Models;

/// <summary>
/// A single timed subtitle/dub segment, carrying the source French text, its English translation,
/// and the runtime state produced while fitting the synthesized voice into its time slot.
/// </summary>
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

    [JsonIgnore]
    public string? SynthesizedAudioPath { get; set; }

    [JsonIgnore]
    public double SynthesizedDurationSeconds { get; set; }

    [JsonIgnore]
    public double AppliedSpeedRatio { get; set; } = 1;

    [JsonIgnore]
    public double SlotDurationSeconds => Math.Max(0.01, (EndMilliseconds - StartMilliseconds) / 1000d);
}
