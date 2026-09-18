using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LocalDub.Utils;

/// <summary>
/// Runs external command-line tools (FFmpeg, whisper.cpp, Python, ollama, ...) as child processes,
/// capturing their standard output/error and translating a non-zero exit code into a
/// <see cref="ProcessExecutionException"/> with the captured diagnostics.
/// </summary>
public sealed class ProcessRunner(ILogger<ProcessRunner> logger)
{
    /// <summary>
    /// Starts <paramref name="executable"/> with the given arguments, waits for it to exit, and
    /// returns its captured output. Throws <see cref="ProcessExecutionException"/> if the process
    /// exits with a non-zero code, or <see cref="FileNotFoundException"/> if it cannot be started.
    /// </summary>
    public async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var argumentList = arguments.ToList();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var item in environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        logger.LogDebug("Exécution : {Executable} {Arguments}", executable, string.Join(' ', argumentList.Select(QuoteForLog)));

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Impossible de démarrer '{executable}'.");
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new FileNotFoundException($"L'exécutable '{executable}' est introuvable.", executable, exception);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var result = new ProcessResult(process.ExitCode, stdout, stderr);
        if (!result.Succeeded)
        {
            throw new ProcessExecutionException(executable, result);
        }

        return result;
    }

    /// <summary>
    /// Searches the PATH environment variable for an executable with the given base name,
    /// appending ".exe" automatically on Windows. Returns null if it cannot be found.
    /// </summary>
    public static string? FindOnPath(string executable)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { executable, $"{executable}.exe" }
            : new[] { executable };

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidate in candidates)
            {
                var path = Path.Combine(directory, candidate);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static string QuoteForLog(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    private static void TryKill(Process process)
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
        }
    }
}

/// <summary>
/// Captured result of a completed process execution.
/// </summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Thrown when an external process started by <see cref="ProcessRunner"/> exits with a non-zero
/// code. The message includes the captured standard error/output for diagnosability.
/// </summary>
public sealed class ProcessExecutionException(string executable, ProcessResult result)
    : Exception(BuildMessage(executable, result))
{
    public ProcessResult Result { get; } = result;

    private static string BuildMessage(string executable, ProcessResult result)
    {
        var diagnostics = string.Join(
            Environment.NewLine,
            new[] { result.StandardError, result.StandardOutput }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()));

        return string.IsNullOrEmpty(diagnostics)
            ? $"'{executable}' a échoué avec le code {result.ExitCode}."
            : $"'{executable}' a échoué avec le code {result.ExitCode}.{Environment.NewLine}{diagnostics}";
    }
}
