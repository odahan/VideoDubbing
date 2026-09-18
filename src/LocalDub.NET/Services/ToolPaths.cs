using LocalDub.Configuration;
using LocalDub.Utils;

namespace LocalDub.Services;

/// <summary>
/// Resolves the file-system locations of every external tool and Python environment used by the
/// pipeline (FFmpeg, whisper.cpp, model files, TTS/separation/Kokoro virtual environments),
/// preferring tools already available on PATH before falling back to the managed installation
/// produced by <see cref="SetupService"/>.
/// </summary>
public sealed class ToolPaths(AppSettings settings, PathResolver paths)
{
    public string ToolsRoot => paths.Resolve(settings.Paths.ToolsRoot);
    public string ModelsRoot => paths.Resolve(settings.Paths.ModelsRoot);
    public string PythonRoot => paths.Resolve(settings.Paths.PythonRoot);

    public string Ffmpeg => Locate("ffmpeg", Path.Combine(ToolsRoot, "ffmpeg", "bin", Executable("ffmpeg")));
    public string Ffprobe => Locate("ffprobe", Path.Combine(ToolsRoot, "ffmpeg", "bin", Executable("ffprobe")));
    public string Whisper => LocateWhisper();
    public string WhisperModel => Path.Combine(ModelsRoot, "whisper", settings.Whisper.ModelFile);
    public string TtsPython => PythonExecutable(Path.Combine(PythonRoot, "tts"));
    public string SeparationPython => PythonExecutable(Path.Combine(PythonRoot, "separation"));
    public string KokoroPython => PythonExecutable(Path.Combine(PythonRoot, "kokoro"));

    private string LocateWhisper()
    {
        var onPath = ProcessRunner.FindOnPath("whisper-cli") ?? ProcessRunner.FindOnPath("main");
        if (onPath is not null)
        {
            return onPath;
        }

        var names = new[]
        {
            Path.Combine(ToolsRoot, "whisper", Executable("whisper-cli")),
            Path.Combine(ToolsRoot, "whisper", "bin", Executable("whisper-cli")),
            Path.Combine(ToolsRoot, "whisper", "Release", Executable("whisper-cli")),
            Path.Combine(ToolsRoot, "whisper", Executable("main"))
        };
        // Falls back to the first candidate path even when none exists yet, so that callers such as
        // DoctorService can report a clear "missing" diagnostic instead of an exception being thrown
        // from a simple property getter. Callers that actually need to run Whisper (WhisperTranscriber)
        // are responsible for validating existence beforehand with a precise error message.
        return names.FirstOrDefault(File.Exists) ?? names[0];
    }

    private static string Locate(string name, string managedPath) => ProcessRunner.FindOnPath(name) ?? managedPath;

    private static string PythonExecutable(string environmentDirectory) => OperatingSystem.IsWindows()
        ? Path.Combine(environmentDirectory, "Scripts", "python.exe")
        : Path.Combine(environmentDirectory, "bin", "python");

    private static string Executable(string name) => OperatingSystem.IsWindows() ? $"{name}.exe" : name;
}
