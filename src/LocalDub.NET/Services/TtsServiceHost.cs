using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using LocalDub.Configuration;
using LocalDub.Utils;
using Microsoft.Extensions.Logging;

namespace LocalDub.Services;

/// <summary>
/// Manages the lifecycle of the local Chatterbox TTS HTTP service: checks whether it is already
/// running, starts it as a child Python process if needed, and stops it on disposal.
/// </summary>
public sealed class TtsServiceHost(
    ToolPaths tools,
    AppSettings settings,
    PathResolver paths,
    IHttpClientFactory httpClientFactory,
    ILogger<TtsServiceHost> logger) : IAsyncDisposable
{
    private Process? _process;
    private string? _activeEngine;

    /// <summary>Returns the endpoint for a supported voice engine.</summary>
    public string GetServiceUrl(string engine) => engine switch
    {
        "chatterbox-turbo" => settings.Tts.ServiceUrl,
        "chatterbox-multilingual-v3" => settings.Tts.MultilingualServiceUrl,
        _ => throw new ArgumentException($"Unsupported voice engine: {engine}", nameof(engine))
    };

    /// <summary>
    /// Ensures the Chatterbox service is reachable, starting it automatically if configured to do
    /// so and it is not already healthy.
    /// </summary>
    public async Task EnsureRunningAsync(CancellationToken cancellationToken, string? engine = null)
    {
        engine ??= settings.Tts.Engine;
        var serviceUrl = GetServiceUrl(engine);
        if (_process is not null && _activeEngine != engine)
        {
            await DisposeAsync();
        }
        if (await IsHealthyAsync(serviceUrl, engine, cancellationToken))
        {
            return;
        }

        if (!settings.Tts.AutoStart)
        {
            throw new InvalidOperationException($"Le service TTS ne répond pas sur {settings.Tts.ServiceUrl}.");
        }

        var python = engine == "chatterbox-multilingual-v3"
            ? Path.Combine(tools.PythonRoot, "multilingual", "Scripts", "python.exe")
            : tools.TtsPython;
        if (!File.Exists(python))
        {
            throw new FileNotFoundException($"The Python environment for {engine} is missing: {python}", python);
        }

        var serviceDirectory = Path.Combine(paths.ProjectRoot, "tts-service");
        var startInfo = new ProcessStartInfo
        {
            FileName = python,
            WorkingDirectory = serviceDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("uvicorn");
        startInfo.ArgumentList.Add(engine == "chatterbox-multilingual-v3" ? "multilingual_app:app" : "app:app");
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(new Uri(serviceUrl).Port.ToString());
        startInfo.Environment["LOCALDUB_MODELS_ROOT"] = tools.ModelsRoot;
        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Impossible de démarrer Chatterbox.");
        _activeEngine = engine;
        _process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data)) logger.LogDebug("Chatterbox : {Message}", eventArgs.Data);
        };
        _process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data)) LogServiceMessage(eventArgs.Data);
        };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        logger.LogInformation("Démarrage du service vocal Chatterbox");
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            if (_process.HasExited)
            {
                throw new InvalidOperationException($"Le service Chatterbox s'est arrêté avec le code {_process.ExitCode}.");
            }

            if (await IsHealthyAsync(serviceUrl, engine, cancellationToken))
            {
                return;
            }
        }

        throw new TimeoutException("Le service Chatterbox n'est pas devenu disponible dans le délai prévu.");
    }

    private void LogServiceMessage(string message)
    {
        if (message.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Traceback", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Exception", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Chatterbox : {Message}", message);
            return;
        }

        logger.LogDebug("Chatterbox : {Message}", message);
    }

    private async Task<bool> IsHealthyAsync(string serviceUrl, string expectedEngine, CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(2);
            using var response = await client.GetAsync($"{serviceUrl.TrimEnd('/')}/health", cancellationToken);
            if (!response.IsSuccessStatusCode) return false;
            var health = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return health.TryGetProperty("status", out var status) && status.GetString() == "ok"
                && health.TryGetProperty("engine", out var engine) && engine.GetString() == expectedEngine
                && health.TryGetProperty("cuda", out var cuda) && cuda.ValueKind == JsonValueKind.True;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_process is { } process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process may have exited between the HasExited check and the Kill call.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The process could not be terminated (e.g. already terminating, insufficient rights).
            }
            finally
            {
                process.Dispose();
                _process = null;
            }
        }

        return ValueTask.CompletedTask;
    }
}
