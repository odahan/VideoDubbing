namespace LocalDub.Models;

/// <summary>Which final artifacts a dubbing run should produce.</summary>
public enum ProductionMode
{
    /// <summary>Produces the dubbed video, WAV, SRT and manifest (default).</summary>
    CompleteVideo,
    /// <summary>Produces only the dubbed WAV, SRT and manifest (no remuxed video).</summary>
    TranslatedWavOnly
}
