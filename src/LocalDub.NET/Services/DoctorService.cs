using LocalDub.Configuration;
using LocalDub.Utils;

namespace LocalDub.Services;

/// <summary>
/// Checks that every external dependency required by LocalDub.NET (tools, models, Python
/// environments, Ollama models) is present and reports a pass/fail diagnostic for each.
/// </summary>
public sealed class DoctorService(ToolPaths tools, AppSettings settings, PathResolver paths, ProcessRunner processRunner, OllamaModelService ollamaModels)
{
    /// <summary>Runs every diagnostic check and prints a report to the console. Returns true only if every check passed.</summary>
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
            await ollamaModels.CheckAsync(settings.Ollama.Model, cancellationToken);
            checks.Add(("Ollama traduction", true, settings.Ollama.Model));
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            checks.Add(("Ollama traduction", false, exception.Message));
        }

        try
        {
            var timingModel = string.IsNullOrWhiteSpace(settings.Ollama.TimingModel) ? settings.Ollama.Model : settings.Ollama.TimingModel;
            await ollamaModels.CheckAsync(timingModel, cancellationToken);
            checks.Add(("Ollama timing", true, timingModel));
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            checks.Add(("Ollama timing", false, exception.Message));
        }

        await CheckRuntimeAsync("FFmpeg runtime", tools.Ffmpeg, ["-version"], checks, cancellationToken);
        await CheckRuntimeAsync("FFprobe runtime", tools.Ffprobe, ["-version"], checks, cancellationToken);
        await CheckRuntimeAsync("Chatterbox / CUDA", tools.TtsPython,
            ["-c", "import torch; from chatterbox.tts_turbo import ChatterboxTurboTTS; import fastapi, uvicorn, soundfile; assert torch.cuda.is_available(), 'CUDA unavailable'; print(torch.__version__, torch.cuda.get_device_name(0))"], checks, cancellationToken);
        await CheckRuntimeAsync("Demucs runtime", tools.SeparationPython,
            ["-c", "import sys, torch, demucs.separate; assert sys.argv[1] != 'cuda' or torch.cuda.is_available(), 'CUDA unavailable'; print(torch.__version__)", settings.Separation.Device], checks, cancellationToken);

        foreach (var check in checks)
        {
            Console.WriteLine($"{(check.Success ? "[OK]" : "[MANQUANT]")} {check.Name} - {check.Detail}");
        }

        return checks.All(item => item.Success);
    }

    private static (string, bool, string) CheckFile(string name, string path) =>
        (name, File.Exists(path), path);

    private async Task CheckRuntimeAsync(string name, string executable, string[] arguments,
        List<(string Name, bool Success, string Detail)> checks, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var result = await processRunner.RunAsync(executable, arguments, cancellationToken: timeout.Token);
            checks.Add((name, true, result.StandardOutput.Trim().Split('\n')[0].Trim()));
        }
        catch (Exception exception) when (exception is FileNotFoundException or ProcessExecutionException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            checks.Add((name, false, exception.Message));
        }
    }
}
