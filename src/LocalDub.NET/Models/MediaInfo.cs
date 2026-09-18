namespace LocalDub.Models;

/// <summary>Media metadata probed from a source video's primary audio stream.</summary>
public sealed record MediaInfo(double DurationSeconds, int AudioSampleRate, int AudioChannels);
