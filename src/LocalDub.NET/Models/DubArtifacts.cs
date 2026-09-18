namespace LocalDub.Models;

/// <summary>Paths of the artifacts produced by a completed dubbing run.</summary>
public sealed record DubArtifacts(
    string? VideoPath,
    string DubbedWavPath,
    string SubtitlePath,
    string ManifestPath,
    string WorkDirectory);
