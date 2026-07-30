namespace LocalDub.Models;

public sealed record DubArtifacts(
    string? VideoPath,
    string DubbedWavPath,
    string SubtitlePath,
    string ManifestPath,
    string WorkDirectory);
