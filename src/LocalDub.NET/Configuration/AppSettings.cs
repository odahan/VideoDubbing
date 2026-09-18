namespace LocalDub.Configuration;

/// <summary>
/// Root configuration object bound from appsettings.json, grouping every setting section used
/// across the LocalDub.NET pipeline (paths, translation, transcription, speech synthesis, audio
/// mixing and vocal separation).
/// </summary>
public sealed class AppSettings
{
    public PathSettings Paths { get; init; } = new();
    public OllamaSettings Ollama { get; init; } = new();
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
    public string OutputRoot { get; init; } = "output";
    public string VoicesFile { get; init; } = "voices/profiles.json";
    public string GlossariesRoot { get; init; } = "glossaries";
}

/// <summary>
/// Connection and generation settings for the local Ollama instance used for translation and
/// segment timing rewrites.
/// </summary>
public sealed class OllamaSettings
{
    public string Endpoint { get; init; } = "http://localhost:11434";
    public string Model { get; init; } = "qwen38OD:latest";
    public int BatchSize { get; init; } = 12;
    public float Temperature { get; init; } = 0.1f;
    public int ContextSize { get; init; } = 16384;
    public int RequestTimeoutMinutes { get; init; } = 10;
    public bool UnloadBeforeTts { get; init; }
    public string TimingModel { get; init; } = "granite4.2:3b";
    public int TimingContextSize { get; init; } = 2048;

    /// <summary>
    /// Enables "thinking" (explicit reasoning) mode on models that support it (e.g. Qwen3).
    /// Disabled by default: translation and trimming are constrained text-transformation tasks
    /// (JSON format, word count) that do not need extended reasoning, and thinking adds latency,
    /// extra token usage, and the risk that the model "leaks" reasoning traces into the structured
    /// output.
    /// </summary>
    public bool EnableThinking { get; init; }
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
}

/// <summary>
/// Settings for the local Chatterbox text-to-speech service (endpoint, timing constraints).
/// </summary>
public sealed class TtsSettings
{
    public string ServiceUrl { get; init; } = "http://127.0.0.1:5055";
    public bool AutoStart { get; init; } = true;
    public string Engine { get; init; } = "chatterbox-turbo";
    public string Language { get; init; } = "en";
    public int RequestTimeoutMinutes { get; init; } = 10;
    public double MaxSpeedRatio { get; init; } = 1.15;
    public double TargetWordsPerSecond { get; init; } = 3.0;

    /// <summary>
    /// Hard upper bound applied to the last-resort speed-up ratio when a segment still does not
    /// fit its slot after the maximum number of rewording attempts. Beyond this ratio, FFmpeg's
    /// "atempo" filter introduces noticeable audio artifacts, so the ratio is clamped instead of
    /// growing unbounded.
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
