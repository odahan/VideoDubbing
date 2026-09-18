namespace LocalDub.Models;

/// <summary>Fully-resolved options for a single "dub" pipeline run.</summary>
public sealed class DubOptions
{
    public required string InputPath { get; init; }
    public AudioMode AudioMode { get; init; } = AudioMode.Separate;
    public ProductionMode ProductionMode { get; init; } = ProductionMode.CompleteVideo;
    public string VoiceProfileId { get; init; } = "michael-us";
    public string? VoiceReferencePath { get; init; }
    public string VoiceVariant { get; init; } = "neutral";
    public string? OllamaModel { get; init; }
    public string? GlossaryName { get; init; }
    public IReadOnlyList<string> PreservedTerms { get; init; } = [];
    public bool NonInteractive { get; init; }
    public bool Overwrite { get; init; }
    public bool KeepWorkFiles { get; init; } = true;
}
