using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using LocalDub.Configuration;
using LocalDub.Utils;
using Microsoft.Extensions.Logging;

namespace LocalDub.Services;

public sealed class SetupService(
    ToolPaths tools,
    AppSettings settings,
    PathResolver paths,
    ProcessRunner processRunner,
    IHttpClientFactory httpClientFactory,
    ILogger<SetupService> logger)
{
    private const string FfmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private const string WhisperReleaseApi = "https://api.github.com/repos/ggml-org/whisper.cpp/releases/latest";
    private const string UvReleaseApi = "https://api.github.com/repos/astral-sh/uv/releases/latest";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(tools.ToolsRoot);
        Directory.CreateDirectory(tools.ModelsRoot);
        Directory.CreateDirectory(tools.PythonRoot);

        await EnsureFfmpegAsync(cancellationToken);
        await EnsureWhisperAsync(cancellationToken);
        await EnsureWhisperModelAsync(cancellationToken);
        await EnsureOllamaModelAsync(settings.Ollama.Model, cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.Ollama.TimingModel)
            && !settings.Ollama.TimingModel.Equals(settings.Ollama.Model, StringComparison.OrdinalIgnoreCase))
        {
            await EnsureOllamaModelAsync(settings.Ollama.TimingModel, cancellationToken);
        }
        var uv = await EnsureUvAsync(cancellationToken);
        await EnsurePythonEnvironmentAsync(uv, "tts", "requirements.txt", "preload_tts.py", requiresCuda: true, cancellationToken: cancellationToken);
        await EnsurePythonEnvironmentAsync(uv, "separation", "requirements-separation.txt", "preload_separation.py", requiresCuda: true, cancellationToken: cancellationToken);
        await EnsurePythonEnvironmentAsync(
            uv,
            "kokoro",
            "requirements-kokoro.txt",
            "preload_kokoro.py",
            requiresCuda: false,
            requiredOutputs:
            [
                Path.Combine(paths.ProjectRoot, "voices", "michael-us.wav"),
                Path.Combine(paths.ProjectRoot, "voices", "adam-us.wav")
            ],
            cancellationToken: cancellationToken);
        logger.LogInformation("Installation terminée. LocalDub peut désormais fonctionner hors ligne.");
    }

    private async Task EnsureFfmpegAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(tools.Ffmpeg))
        {
            logger.LogInformation("FFmpeg est disponible");
            return;
        }

        EnsureWindowsForManagedBinaries();
        var archive = Path.Combine(tools.ToolsRoot, "ffmpeg.zip");
        var extraction = Path.Combine(tools.ToolsRoot, "ffmpeg-extract");
        await DownloadAsync(FfmpegUrl, archive, cancellationToken);
        RecreateDirectory(extraction);
        ZipFile.ExtractToDirectory(archive, extraction);
        var bin = Directory.EnumerateDirectories(extraction, "bin", SearchOption.AllDirectories).FirstOrDefault()
                  ?? throw new InvalidDataException("L'archive FFmpeg ne contient pas de dossier bin.");
        var target = Path.Combine(tools.ToolsRoot, "ffmpeg", "bin");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(bin))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        File.Delete(archive);
        Directory.Delete(extraction, recursive: true);
    }

    private async Task EnsureWhisperAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(tools.Whisper))
        {
            logger.LogInformation("Whisper.cpp est disponible");
            return;
        }

        EnsureWindowsForManagedBinaries();
        var release = await GetReleaseAsync(WhisperReleaseApi, cancellationToken);
        var asset = release.Assets.FirstOrDefault(item => item.Name.Equals("whisper-cublas-12.4.0-bin-x64.zip", StringComparison.OrdinalIgnoreCase))
                    ?? release.Assets.FirstOrDefault(item => item.Name.Equals("whisper-bin-x64.zip", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("Aucun binaire Windows x64 de whisper.cpp n'est publié dans la dernière version.");
        var archive = Path.Combine(tools.ToolsRoot, "whisper.zip");
        var target = Path.Combine(tools.ToolsRoot, "whisper");
        await DownloadAsync(asset.BrowserDownloadUrl, archive, cancellationToken);
        RecreateDirectory(target);
        ZipFile.ExtractToDirectory(archive, target);
        File.Delete(archive);
    }

    private async Task EnsureWhisperModelAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(tools.WhisperModel))
        {
            logger.LogInformation("Modèle Whisper {Model} disponible", settings.Whisper.ModelName);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(tools.WhisperModel)!);
        await DownloadAsync(settings.Whisper.ModelUrl, tools.WhisperModel, cancellationToken);
    }

    private async Task<string> EnsureUvAsync(CancellationToken cancellationToken)
    {
        var uv = Path.Combine(tools.ToolsRoot, "uv", OperatingSystem.IsWindows() ? "uv.exe" : "uv");
        if (File.Exists(uv))
        {
            return uv;
        }

        EnsureWindowsForManagedBinaries();
        var release = await GetReleaseAsync(UvReleaseApi, cancellationToken);
        var asset = release.Assets.FirstOrDefault(item => item.Name.Equals("uv-x86_64-pc-windows-msvc.zip", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("Le binaire Windows x64 de uv est introuvable.");
        var archive = Path.Combine(tools.ToolsRoot, "uv.zip");
        var target = Path.Combine(tools.ToolsRoot, "uv");
        await DownloadAsync(asset.BrowserDownloadUrl, archive, cancellationToken);
        RecreateDirectory(target);
        ZipFile.ExtractToDirectory(archive, target);
        File.Delete(archive);
        return uv;
    }

    private async Task EnsureOllamaModelAsync(string model, CancellationToken cancellationToken)
    {
        try
        {
            await processRunner.RunAsync("ollama", ["show", model], cancellationToken: cancellationToken);
            logger.LogInformation("Modèle Ollama {Model} disponible", model);
        }
        catch (ProcessExecutionException)
        {
            logger.LogInformation("Téléchargement du modèle Ollama {Model}", model);
            await processRunner.RunAsync("ollama", ["pull", model], cancellationToken: cancellationToken);
        }
    }

    private async Task EnsurePythonEnvironmentAsync(
        string uv,
        string name,
        string requirementsFile,
        string preloadScript,
        bool requiresCuda,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? requiredOutputs = null)
    {
        var environment = Path.Combine(tools.PythonRoot, name);
        var python = OperatingSystem.IsWindows()
            ? Path.Combine(environment, "Scripts", "python.exe")
            : Path.Combine(environment, "bin", "python");
        var serviceDirectory = Path.Combine(paths.ProjectRoot, "tts-service");
        var requirementsPath = Path.Combine(serviceDirectory, requirementsFile);
        var preloadPath = Path.Combine(serviceDirectory, preloadScript);
        var setupFingerprint = await ComputeSetupFingerprintAsync(
            requirementsPath, preloadPath, requiresCuda, cancellationToken);

        if (!File.Exists(python))
        {
            logger.LogInformation("Création de l'environnement Python {Name}", name);
            await processRunner.RunAsync(uv, ["python", "install", "3.11"], paths.ProjectRoot, cancellationToken: cancellationToken);
            await processRunner.RunAsync(uv, ["venv", "--python", "3.11", environment], paths.ProjectRoot, cancellationToken: cancellationToken);
        }

        var marker = Path.Combine(environment, ".localdub-ready");
        if (File.Exists(marker))
        {
            var installedFingerprint = (await File.ReadAllTextAsync(marker, cancellationToken)).Trim();
            var runtimeReady = !requiresCuda || await IsCudaAvailableAsync(python, cancellationToken);
            var outputsReady = requiredOutputs is null || requiredOutputs.All(File.Exists);
            if (installedFingerprint == setupFingerprint && runtimeReady && outputsReady)
            {
                logger.LogInformation(
                    requiresCuda
                        ? "Environnement Python {Name} disponible avec CUDA"
                        : "Environnement Python {Name} disponible",
                    name);
                return;
            }

            logger.LogWarning("L'environnement Python {Name} est incomplet ; réparation", name);
            File.Delete(marker);
        }

        await processRunner.RunAsync(uv,
            ["pip", "install", "--python", python, "-r", requirementsPath],
            paths.ProjectRoot, cancellationToken: cancellationToken);
        if (requiresCuda)
        {
            await InstallCudaPyTorchAsync(uv, python, cancellationToken);
            await VerifyCudaAsync(python, cancellationToken);
        }
        await processRunner.RunAsync(python, [preloadPath], serviceDirectory, cancellationToken: cancellationToken);
        if (requiredOutputs is not null && requiredOutputs.Any(path => !File.Exists(path)))
        {
            throw new InvalidDataException($"L'environnement {name} n'a pas produit tous les fichiers attendus.");
        }
        await File.WriteAllTextAsync(marker, setupFingerprint, cancellationToken);
    }

    private static async Task<string> ComputeSetupFingerprintAsync(
        string requirementsPath,
        string preloadPath,
        bool requiresCuda,
        CancellationToken cancellationToken)
    {
        var requirements = await File.ReadAllTextAsync(requirementsPath, cancellationToken);
        var preload = await File.ReadAllTextAsync(preloadPath, cancellationToken);
        var content = $"cuda={requiresCuda}\n{requirements}\n---preload---\n{preload}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    private async Task InstallCudaPyTorchAsync(string uv, string python, CancellationToken cancellationToken)
    {
        if (await IsCudaAvailableAsync(python, cancellationToken))
        {
            logger.LogInformation("PyTorch CUDA est déjà opérationnel");
            return;
        }

        logger.LogInformation("Installation de PyTorch 2.6 avec CUDA 12.4");
        await processRunner.RunAsync(uv,
        [
            "pip", "install",
            "--python", python,
            "--reinstall",
            "--no-deps",
            "torch==2.6.0",
            "torchvision==0.21.0",
            "torchaudio==2.6.0",
            "--index-url", "https://download.pytorch.org/whl/cu124"
        ], paths.ProjectRoot, cancellationToken: cancellationToken);
    }

    private async Task<bool> IsCudaAvailableAsync(string python, CancellationToken cancellationToken)
    {
        try
        {
            await VerifyCudaAsync(python, cancellationToken);
            return true;
        }
        catch (ProcessExecutionException)
        {
            return false;
        }
    }

    private Task VerifyCudaAsync(string python, CancellationToken cancellationToken) =>
        processRunner.RunAsync(python,
        [
            "-c",
            "import torch; assert torch.version.cuda is not None, f'CPU-only PyTorch: {torch.__version__}'; assert torch.cuda.is_available(), f'CUDA build {torch.version.cuda} cannot access the GPU'; print(f'PyTorch {torch.__version__} / CUDA {torch.version.cuda} / {torch.cuda.get_device_name(0)}')"
        ], paths.ProjectRoot, cancellationToken: cancellationToken);

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        logger.LogInformation("Téléchargement de {File}", Path.GetFileName(destination));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = $"{destination}.download";
        using var client = httpClientFactory.CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LocalDub.NET/1.0");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(temporary))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        File.Move(temporary, destination, overwrite: true);
    }

    private async Task<GitHubRelease> GetReleaseAsync(string url, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LocalDub.NET/1.0");
        return await client.GetFromJsonAsync<GitHubRelease>(url, cancellationToken)
               ?? throw new InvalidDataException($"Réponse GitHub invalide : {url}");
    }

    private static void EnsureWindowsForManagedBinaries()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("L'installation automatique de cette V1 cible Windows x64.");
        }
    }

    private static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        Directory.CreateDirectory(path);
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;
    }
}
