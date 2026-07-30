using LocalDub.Configuration;
using LocalDub.Utils;

namespace LocalDub.Services;

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
        return names.FirstOrDefault(File.Exists) ?? names[0];
    }

    private static string Locate(string name, string managedPath) => ProcessRunner.FindOnPath(name) ?? managedPath;

    private static string PythonExecutable(string environmentDirectory) => OperatingSystem.IsWindows()
        ? Path.Combine(environmentDirectory, "Scripts", "python.exe")
        : Path.Combine(environmentDirectory, "bin", "python");

    private static string Executable(string name) => OperatingSystem.IsWindows() ? $"{name}.exe" : name;
}
