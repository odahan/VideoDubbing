namespace LocalDub.Configuration;

/// <summary>
/// Root configuration object bound from appsettings.json, grouping every setting section used
/// across the LocalDub.NET pipeline (paths, translation, transcription, speech synthesis, audio
/// mixing and vocal separation).
/// </summary>
public sealed class AppSettings
{
    public PathSettings Paths { get; init; } = new();
    public OpenAiSettings OpenAI { get; init; } = new();
    public WhisperSettings Whisper { get; init; } = new();
    public TtsSettings Tts { get; init; } = new();
    public AudioSettings Audio { get; init; } = new();
    public SeparationSettings Separation { get; init; } = new();
}

/// <summary>
/// Filesystem locations (relative to the project root unless rooted) used to resolve managed
/// tools, models, Python environments and user-editable data files.
/// </summary>
public sealed class PathSettings
{
    public string ToolsRoot { get; init; } = ".localdub/tools";
    public string ModelsRoot { get; init; } = ".localdub/models";
    public string PythonRoot { get; init; } = ".localdub/python";
    public string OutputRoot { get; init; } = "output/openai";
    public string VoicesFile { get; init; } = "voices/profiles.json";
    public string GlossariesRoot { get; init; } = "glossaries";
}

/// <summary>Model preferences and request limits for ChatGPT plan inference.</summary>
public sealed class OpenAiSettings
{
    public string Model { get; init; } = "gpt-6-luna";
    public string? TimingModel { get; init; }
    public int BatchSize { get; init; } = 12;
    public int RequestTimeoutMinutes { get; init; } = 5;
}

/// <summary>
/// Settings for the local whisper.cpp transcription engine (model, language, threading).
/// </summary>
public sealed class WhisperSettings
{
    public string ModelName { get; init; } = "large-v3-turbo";
    public string ModelFile { get; init; } = "ggml-large-v3-turbo.bin";
    public string ModelUrl { get; init; } = string.Empty;
    public string Language { get; init; } = "fr";
    public int Threads { get; init; } = 8;

    /// <summary>Bounds transcription chunks so a long utterance stays suitable for segment TTS.</summary>
    public int MaxSegmentCharacters { get; init; } = 200;
}

/// <summary>
/// Settings for the local Chatterbox text-to-speech service (endpoint, timing constraints).
/// </summary>
public sealed class TtsSettings
{
    public string ServiceUrl { get; init; } = "http://127.0.0.1:5055";
    public string MultilingualServiceUrl { get; init; } = "http://127.0.0.1:5056";
    public bool AutoStart { get; init; } = true;
    public string Engine { get; init; } = "chatterbox-turbo";
    public string Language { get; init; } = "en";
    public int RequestTimeoutMinutes { get; init; } = 10;
    public double MaxSpeedRatio { get; init; } = 1.15;
    public double TargetWordsPerSecond { get; init; } = 3.0;

    /// <summary>
    /// Hard upper bound for the last-resort speed-up after rewording attempts. A segment that
    /// still cannot fit at this speed stops rendering rather than silently losing spoken words.
    /// </summary>
    public double AbsoluteMaxSpeedRatio { get; init; } = 2.5;
}

/// <summary>
/// Output audio format and mixing parameters (sample rate, bit depth, ducking, output codec).
/// </summary>
public sealed class AudioSettings
{
    public int SampleRate { get; init; } = 48000;
    public int BitDepth { get; init; } = 24;
    public double DuckLevelDb { get; init; } = -24;
    public int DuckingFadeMilliseconds { get; init; } = 150;
    public string OutputCodec { get; init; } = "aac";
    public string OutputBitrate { get; init; } = "320k";
}

/// <summary>
/// Settings for the Demucs-based vocal/accompaniment source separation step.
/// </summary>
public sealed class SeparationSettings
{
    public string Model { get; init; } = "htdemucs";
    public string Device { get; init; } = "cuda";
}
