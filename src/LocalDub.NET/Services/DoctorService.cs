using LocalDub.Configuration;
using LocalDub.Utils;

namespace LocalDub.Services;

public sealed class DoctorService(ToolPaths tools, AppSettings settings, PathResolver paths, ProcessRunner processRunner)
{
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        var checks = new List<(string Name, bool Success, string Detail)>
        {
            CheckFile("FFmpeg", tools.Ffmpeg),
            CheckFile("FFprobe", tools.Ffprobe),
            CheckFile("Whisper.cpp", tools.Whisper),
            CheckFile("Modèle Whisper", tools.WhisperModel),
            CheckFile("Python Chatterbox", tools.TtsPython),
            CheckFile("Python séparation", tools.SeparationPython),
            CheckFile("Python Kokoro", tools.KokoroPython),
            CheckFile("Voix Michael US", paths.Resolve("voices/michael-us.wav")),
            CheckFile("Voix Adam US", paths.Resolve("voices/adam-us.wav"))
        };

        try
        {
            await processRunner.RunAsync("ollama", ["show", settings.Ollama.Model], cancellationToken: cancellationToken);
            checks.Add(("Ollama traduction", true, settings.Ollama.Model));
        }
        catch (Exception exception) when (exception is FileNotFoundException or ProcessExecutionException)
        {
            checks.Add(("Ollama traduction", false, exception.Message));
        }

        try
        {
            await processRunner.RunAsync("ollama", ["show", settings.Ollama.TimingModel], cancellationToken: cancellationToken);
            checks.Add(("Ollama timing", true, settings.Ollama.TimingModel));
        }
        catch (Exception exception) when (exception is FileNotFoundException or ProcessExecutionException)
        {
            checks.Add(("Ollama timing", false, exception.Message));
        }

        foreach (var check in checks)
        {
            Console.WriteLine($"{(check.Success ? "[OK]" : "[MANQUANT]")} {check.Name} - {check.Detail}");
        }

        return checks.All(item => item.Success);
    }

    private static (string, bool, string) CheckFile(string name, string path) =>
        (name, File.Exists(path), path);
}
