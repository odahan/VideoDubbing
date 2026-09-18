namespace LocalDub.Models;

/// <summary>Result of transcribing a source audio track: full text, timed segments, and the raw SRT path.</summary>
public sealed record TranscriptionResult(
    string RawText,
    IReadOnlyList<DubSegment> Segments,
    string SourceSrtPath);
