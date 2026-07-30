namespace LocalDub.Configuration;

public sealed class AppSettings
{
    public PathSettings Paths { get; init; } = new();
    public OllamaSettings Ollama { get; init; } = new();
    public WhisperSettings Whisper { get; init; } = new();
    public TtsSettings Tts { get; init; } = new();
    public AudioSettings Audio { get; init; } = new();
    public SeparationSettings Separation { get; init; } = new();
}

public sealed class PathSettings
{
    public string ToolsRoot { get; init; } = ".localdub/tools";
    public string ModelsRoot { get; init; } = ".localdub/models";
    public string PythonRoot { get; init; } = ".localdub/python";
    public string OutputRoot { get; init; } = "output";
    public string VoicesFile { get; init; } = "voices/profiles.json";
    public string GlossariesRoot { get; init; } = "glossaries";
}

public sealed class OllamaSettings
{
    public string Endpoint { get; init; } = "http://localhost:11434";
    public string Model { get; init; } = "gemma4:12b";
    public int BatchSize { get; init; } = 12;
    public float Temperature { get; init; } = 0.1f;
    public int ContextSize { get; init; } = 16384;
    public int RequestTimeoutMinutes { get; init; } = 10;
    public bool UnloadBeforeTts { get; init; }
    public string TimingModel { get; init; } = "granite4:3b";
    public int TimingContextSize { get; init; } = 2048;
}

public sealed class WhisperSettings
{
    public string ModelName { get; init; } = "large-v3-turbo";
    public string ModelFile { get; init; } = "ggml-large-v3-turbo.bin";
    public string ModelUrl { get; init; } = string.Empty;
    public string Language { get; init; } = "fr";
    public int Threads { get; init; } = 8;
}

public sealed class TtsSettings
{
    public string ServiceUrl { get; init; } = "http://127.0.0.1:5055";
    public bool AutoStart { get; init; } = true;
    public string Engine { get; init; } = "chatterbox-turbo";
    public string Language { get; init; } = "en";
    public int RequestTimeoutMinutes { get; init; } = 10;
    public double MaxSpeedRatio { get; init; } = 1.15;
    public double TargetWordsPerSecond { get; init; } = 3.0;
}

public sealed class AudioSettings
{
    public int SampleRate { get; init; } = 48000;
    public int BitDepth { get; init; } = 24;
    public double DuckLevelDb { get; init; } = -24;
    public int DuckingFadeMilliseconds { get; init; } = 150;
    public string OutputCodec { get; init; } = "aac";
    public string OutputBitrate { get; init; } = "320k";
}

public sealed class SeparationSettings
{
    public string Model { get; init; } = "htdemucs";
    public string Device { get; init; } = "cuda";
}
