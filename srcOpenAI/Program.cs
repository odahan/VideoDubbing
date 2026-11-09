using LocalDub.Cli;
using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.OpenAI;
using LocalDub.Services;
using LocalDub.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalDub;

/// <summary>Runs the OpenAI edition against the shared LocalDub audio installation.</summary>
public static class Program
{
    internal static string EditionName { get; } =
        $"LocalDub OpenAI {typeof(Program).Assembly.GetName().Version!.ToString(3)}";

    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += Cancel;
        try
        {
            if (args is ["--version"])
            {
                Console.WriteLine(EditionName);
                return 0;
            }
            if (args.FirstOrDefault() == "help" || args.Contains("--help"))
            {
                PrintHelp();
                return 0;
            }

            var arguments = args.Length == 0 ? new CliArguments { Command = "menu" } : CliParser.Parse(args);
            var root = FindSharedRoot(arguments.Get("shared-root"));
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.openai.json")
                .Build();
            var settings = configuration.Get<AppSettings>() ?? throw new InvalidDataException("Invalid application settings.");
            await using var services = ConfigureServices(settings, root);
            if (arguments.Command == "menu")
            {
                return await ConsoleMenu.RunAsync(
                    services.GetRequiredService<OpenAiAccountSession>(),
                    services.GetRequiredService<VoiceProfileService>(),
                    services.GetRequiredService<PathResolver>().Resolve(settings.Paths.GlossariesRoot),
                    settings.OpenAI,
                    (command, models) => ExecuteAsync(command, services, settings, cancellation.Token, models),
                    cancellation.Token);
            }
            return await ExecuteAsync(arguments, services, settings, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error: {DescribeError(error)}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
        }

        void Cancel(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        }
    }

    private static async Task<int> ExecuteAsync(
        CliArguments arguments, ServiceProvider services, AppSettings settings, CancellationToken cancellationToken,
        IReadOnlyList<OpenAiAccountModel>? accountModels = null)
    {
        if (arguments.Command == "doctor")
        {
            return CheckAudioInstallation(services.GetRequiredService<ToolPaths>(), services.GetRequiredService<PathResolver>());
        }

        if (arguments.Command is not ("sign-in" or "sign-out" or "models" or "dub"))
        {
            throw new ArgumentException($"Unknown command: {arguments.Command}");
        }

        var account = services.GetRequiredService<OpenAiAccountSession>();
        using var sessionLock = OpenAiAccountSession.LockUserSession();
        if (arguments.Command == "sign-in")
        {
            Console.WriteLine("Continue with ChatGPT in your browser and authorize use of your plan.");
            await account.SignInAsync(null, cancellationToken);
            if (account.StatusKey != "OpenAiConnected")
            {
                throw new InvalidOperationException("Connected, but permission to use the ChatGPT plan was not granted.");
            }
            Console.WriteLine("ChatGPT plan usage enabled.");
            return 0;
        }

        if (arguments.Command == "sign-out")
        {
            var revoked = await account.ResetAsync(cancellationToken);
            Console.WriteLine(revoked ? "ChatGPT connection reset." : "Local connection reset. OpenAI could not confirm remote revocation.");
            return revoked ? 0 : 1;
        }

        if (account.StatusKey != "OpenAiConnected")
        {
            throw new InvalidOperationException("Run sign-in and grant permission to use your ChatGPT plan first.");
        }

        if (arguments.Command == "models")
        {
            foreach (var model in await account.GetModelsAsync(cancellationToken))
            {
                Console.WriteLine($"{model.Slug}  {model.DisplayName}");
            }
            return 0;
        }

        var input = Path.GetFullPath(arguments.Get("input") ?? throw new ArgumentException("Specify --input VIDEO."));
        if (!File.Exists(input))
        {
            throw new FileNotFoundException($"Source video was not found: {input}");
        }
        var audioMode = arguments.GetAudioMode();
        var productionMode = arguments.GetProductionMode();
        var baseSpeed = NarrationPace.ParseBaseRatio(arguments.Get("base-speed"), settings.Tts.AbsoluteMaxSpeedRatio);
        var modelId = arguments.Get("model") ?? settings.OpenAI.Model;
        var timingModel = arguments.Get("timing-model") ?? settings.OpenAI.TimingModel;
        var transcript = arguments.Get("transcript") is { } path ? Path.GetFullPath(path) : null;
        if (transcript is not null && !File.Exists(transcript))
        {
            throw new FileNotFoundException($"Source transcript was not found: {transcript}");
        }

        var models = accountModels ?? await account.GetModelsAsync(cancellationToken);
        RequireModel(models, modelId);
        if (timingModel is not null)
        {
            RequireModel(models, timingModel);
        }

        var options = new DubOptions
        {
            InputPath = input,
            TranscriptPath = transcript,
            AudioMode = audioMode,
            ProductionMode = productionMode,
            VoiceProfileId = arguments.Get("voice") ?? "michael-us",
            VoiceReferencePath = arguments.Get("reference"),
            VoiceVariant = arguments.Get("voice-variant") ?? "neutral",
            GlossaryName = arguments.Get("glossary"),
            PreservedTerms = (arguments.Get("preserve") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            Model = modelId,
            TimingModel = timingModel,
            BaseSpeedRatio = baseSpeed,
            Overwrite = arguments.Has("overwrite")
        };

        var result = await services.GetRequiredService<DubPipeline>().RunAsync(options, cancellationToken);
        if (result.VideoPath is not null)
        {
            Console.WriteLine($"Video: {result.VideoPath}");
        }
        Console.WriteLine($"WAV: {result.DubbedWavPath}");
        Console.WriteLine($"Subtitles: {result.SubtitlePath}");
        Console.WriteLine($"Manifest: {result.ManifestPath}");
        Console.WriteLine($"Work directory: {result.WorkDirectory}");
        return 0;
    }

    private static void RequireModel(IReadOnlyList<OpenAiAccountModel> models, string slug)
    {
        if (!models.Any(model => string.Equals(model.Slug, slug, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Model '{slug}' is not offered by this account. Run models to see available slugs.");
        }
    }

    internal static string DescribeError(Exception error) => error.Message switch
    {
        "OpenAiReconnect" => "The ChatGPT session needs attention. Sign in again.",
        "OpenAiPermissionMissing" => "Allow the application to use your ChatGPT plan when you sign in.",
        "OpenAiAuthorizationFailed" => "ChatGPT sign-in was declined or could not be verified.",
        "OpenAiSignInTimeout" => "The browser sign-in timed out.",
        "OpenAiModelsFailed" => "The model catalogue could not be retrieved for this ChatGPT connection.",
        "OpenAiIdentityMismatch" => "A different ChatGPT account was selected. Reset the saved connection before switching accounts.",
        "OpenAiStorageFailed" => "The ChatGPT connection could not be saved in Windows protected storage.",
        _ => error.Message
    };

    private static int CheckAudioInstallation(ToolPaths tools, PathResolver paths)
    {
        var checks = new (string Name, string Path)[]
        {
            ("FFmpeg", tools.Ffmpeg), ("FFprobe", tools.Ffprobe), ("whisper.cpp", tools.Whisper),
            ("Whisper model", tools.WhisperModel), ("Chatterbox Python", tools.TtsPython),
            ("Demucs Python", tools.SeparationPython), ("Chatterbox service", paths.Resolve("tts-service/app.py"))
        };
        var failures = 0;
        foreach (var check in checks)
        {
            var found = File.Exists(check.Path);
            Console.WriteLine($"{(found ? "OK" : "Missing")}  {check.Name}: {check.Path}");
            if (!found)
            {
                failures++;
            }
        }
        return failures == 0 ? 0 : 1;
    }

    private static string FindSharedRoot(string? explicitRoot)
    {
        if (explicitRoot is not null)
        {
            var root = Path.GetFullPath(explicitRoot);
            return Directory.Exists(Path.Combine(root, "tts-service"))
                ? root
                : throw new DirectoryNotFoundException($"The shared root has no tts-service directory: {root}");
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LocalDub.NET.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tts-service")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Shared audio installation not found. Specify --shared-root PATH.");
    }

    private static ServiceProvider ConfigureServices(AppSettings settings, string root)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(new PathResolver(root));
        services.AddLogging(builder => builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        }).AddFilter("System.Net.Http.HttpClient", LogLevel.Warning));
        services.AddHttpClient();
        services.AddHttpClient("OpenAI", client => client.Timeout = TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton(provider => new OpenAiAccountSession(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAI"), new DpapiSecretVault()));
        services.AddSingleton<OpenAiResponsesClient>();
        services.AddSingleton<OpenAiTranslator>();
        services.AddSingleton<ProcessRunner>();
        services.AddSingleton<ToolPaths>();
        services.AddSingleton<MediaProbe>();
        services.AddSingleton<AudioExtractor>();
        services.AddSingleton<SourceSeparator>();
        services.AddSingleton<WhisperTranscriber>();
        services.AddSingleton<InitialSpeechAlignment>();
        services.AddSingleton<TranscriptRepeatRepair>();
        services.AddSingleton<GlossaryService>();
        services.AddSingleton<VoiceProfileService>();
        services.AddSingleton<TtsServiceHost>();
        services.AddSingleton<ChatterboxSynthesizer>();
        services.AddSingleton<AudioTimelineBuilder>();
        services.AddSingleton<SubtitleGenerator>();
        services.AddSingleton<VideoMixer>();
        services.AddSingleton<DubPipeline>();
        return services.BuildServiceProvider();
    }

    private static void PrintHelp() => Console.WriteLine($"""
        {EditionName} | French to US English dubbing

        localdub-openai --version
        localdub-openai menu
        localdub-openai doctor
        localdub-openai sign-in
        localdub-openai models
        localdub-openai sign-out
        localdub-openai dub --input VIDEO [options]

        --model SLUG          Translation model (default: gpt-6-luna)
        --timing-model SLUG   Optional model for shortening; defaults to the translation model
        --base-speed RATIO    Base narration speed for this video (1.00 default, 1.10 for +10%)
        --transcript JSON     Reuse the source segments from a LocalDub segments file or manifest
        --production MODE     video (default) or wav
        --audio-mode MODE     separate (default), duck, or external-mix
        --voice ID            Voice profile (default: michael-us)
        --reference WAV       Override the reference audio
        --voice-variant NAME  neutral, stable, or expressive
        --glossary NAME       Glossary name or path
        --preserve LIST       Comma-separated terms to preserve
        --overwrite           Replace the OpenAI edition's previous exports
        --shared-root PATH    Root of the existing LocalDub installation

        Only source and translated text, glossary terms and timing constraints go to OpenAI.
        Audio, video, and voice references are processed locally.
        """);
}
