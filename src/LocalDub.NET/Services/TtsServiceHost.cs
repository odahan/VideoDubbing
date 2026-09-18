using System.Diagnostics;
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

    /// <summary>
    /// Ensures the Chatterbox service is reachable, starting it automatically if configured to do
    /// so and it is not already healthy.
    /// </summary>
    public async Task EnsureRunningAsync(CancellationToken cancellationToken)
    {
        if (await IsHealthyAsync(cancellationToken))
        {
            return;
        }

        if (!settings.Tts.AutoStart)
        {
            throw new InvalidOperationException($"Le service TTS ne répond pas sur {settings.Tts.ServiceUrl}.");
        }

        if (!File.Exists(tools.TtsPython))
        {
            throw new FileNotFoundException("L'environnement Chatterbox est absent. Exécutez 'localdub setup'.", tools.TtsPython);
        }

        var serviceDirectory = Path.Combine(paths.ProjectRoot, "tts-service");
        var startInfo = new ProcessStartInfo
        {
            FileName = tools.TtsPython,
            WorkingDirectory = serviceDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("uvicorn");
        startInfo.ArgumentList.Add("app:app");
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(new Uri(settings.Tts.ServiceUrl).Port.ToString());
        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Impossible de démarrer Chatterbox.");
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

            if (await IsHealthyAsync(cancellationToken))
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

        // Uvicorn, Hugging Face et les barres de progression utilisent stderr
        // même en fonctionnement normal. Ils restent accessibles en niveau Debug.
        logger.LogDebug("Chatterbox : {Message}", message);
    }

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(2);
            using var response = await client.GetAsync($"{settings.Tts.ServiceUrl.TrimEnd('/')}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
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
        if (_process is { HasExited: false } process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
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
            }
        }

        return ValueTask.CompletedTask;
    }
}
