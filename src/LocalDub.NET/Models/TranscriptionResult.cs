namespace LocalDub.Models;

public sealed record TranscriptionResult(
    string RawText,
    IReadOnlyList<DubSegment> Segments,
    string SourceSrtPath);
