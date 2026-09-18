using LocalDub.Models;

namespace LocalDub.Cli;

/// <summary>Parsed command-line invocation: a command name plus its "--name value" options.</summary>
public sealed class CliArguments
{
    public string Command { get; init; } = "help";
    public Dictionary<string, string?> Options { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string name) => Options.GetValueOrDefault(name);
    public bool Has(string name) => Options.ContainsKey(name);

    public AudioMode GetAudioMode(AudioMode fallback = AudioMode.Separate)
    {
        var value = Get("audio-mode");
        return value?.ToLowerInvariant() switch
        {
            "separate" => AudioMode.Separate,
            "duck" => AudioMode.Duck,
            "external-mix" => AudioMode.ExternalMix,
            null => fallback,
            _ => throw new ArgumentException("--audio-mode doit valoir separate, duck ou external-mix.")
        };
    }

    public ProductionMode GetProductionMode(ProductionMode fallback = ProductionMode.CompleteVideo)
    {
        var value = Get("production");
        return value?.ToLowerInvariant() switch
        {
            "video" or "complete" => ProductionMode.CompleteVideo,
            "wav" or "wav-only" => ProductionMode.TranslatedWavOnly,
            null => fallback,
            _ => throw new ArgumentException("--production doit valoir video ou wav.")
        };
    }
}
