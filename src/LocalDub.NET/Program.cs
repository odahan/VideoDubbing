using LocalDub.Cli;
using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Services;
using LocalDub.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalDub;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var root = PathResolver.FindProjectRoot();
            var configuration = new ConfigurationBuilder()
                .SetBasePath(root)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();
            var settings = configuration.Get<AppSettings>()
                           ?? throw new InvalidDataException("appsettings.json est invalide.");

            var services = ConfigureServices(settings, root);
            await using var provider = services.BuildServiceProvider();
            var parsed = CliParser.Parse(args);
            return await ExecuteAsync(parsed, provider, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Opération annulée.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Erreur : {exception.Message}");
            return 1;
        }
    }

    private static ServiceCollection ConfigureServices(AppSettings settings, string root)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(new PathResolver(root));
        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        });
        services.AddHttpClient();
        services.AddSingleton<ProcessRunner>();
        services.AddSingleton<ToolPaths>();
        services.AddSingleton<MediaProbe>();
        services.AddSingleton<AudioExtractor>();
        services.AddSingleton<SourceSeparator>();
        services.AddSingleton<WhisperTranscriber>();
        services.AddSingleton<GlossaryService>();
        services.AddSingleton<OllamaTranslator>();
        services.AddSingleton<VoiceProfileService>();
        services.AddSingleton<TtsServiceHost>();
        services.AddSingleton<ChatterboxSynthesizer>();
        services.AddSingleton<AudioTimelineBuilder>();
        services.AddSingleton<SubtitleGenerator>();
        services.AddSingleton<VideoMixer>();
        services.AddSingleton<SetupService>();
        services.AddSingleton<DoctorService>();
        services.AddSingleton<DubPipeline>();
        services.AddSingleton<InteractiveConsole>();
        return services;
    }

    private static async Task<int> ExecuteAsync(CliArguments arguments, IServiceProvider services, CancellationToken cancellationToken)
    {
        switch (arguments.Command)
        {
            case "setup":
                await services.GetRequiredService<SetupService>().RunAsync(cancellationToken);
                return 0;
            case "doctor":
                return await services.GetRequiredService<DoctorService>().RunAsync(cancellationToken) ? 0 : 1;
            case "voices-audition":
                await AuditionAsync(arguments, services, cancellationToken);
                return 0;
            case "dub":
                var options = await services.GetRequiredService<InteractiveConsole>().BuildDubOptionsAsync(arguments, cancellationToken);
                var artifacts = await services.GetRequiredService<DubPipeline>().RunAsync(options, cancellationToken);
                PrintArtifacts(artifacts);
                return 0;
            case "help":
            case "--help":
                PrintHelp();
                return 0;
            default:
                throw new ArgumentException($"Commande inconnue : {arguments.Command}");
        }
    }

    private static async Task AuditionAsync(CliArguments arguments, IServiceProvider services, CancellationToken cancellationToken)
    {
        const string defaultText = "Welcome. In this video, we're going to explore a new way to build intelligent applications and musical instruments.";
        var profileService = services.GetRequiredService<VoiceProfileService>();
        var profile = await profileService.GetRequiredAsync(arguments.Get("voice") ?? "michael-us", cancellationToken);
        profile = profileService.ApplyOverrides(profile, arguments.Get("reference"), "neutral");

        var output = arguments.Get("output") ?? Path.Combine("voices", "previews");
        var files = await services.GetRequiredService<ChatterboxSynthesizer>().AuditionAsync(
            arguments.Get("text") ?? defaultText, profile, Path.GetFullPath(output), cancellationToken);
        Console.WriteLine("Prévisualisations créées :");
        foreach (var file in files)
        {
            Console.WriteLine($"  {file}");
        }
    }

    private static void PrintArtifacts(DubArtifacts artifacts)
    {
        Console.WriteLine();
        Console.WriteLine("Fichiers créés :");
        if (artifacts.VideoPath is not null)
        {
            Console.WriteLine($"  Vidéo : {artifacts.VideoPath}");
        }
        Console.WriteLine($"  WAV   : {artifacts.DubbedWavPath}");
        Console.WriteLine($"  SRT   : {artifacts.SubtitlePath}");
        Console.WriteLine($"  JSON  : {artifacts.ManifestPath}");
        Console.WriteLine($"  Travail intermédiaire : {artifacts.WorkDirectory}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            LocalDub.NET - doublage local français vers anglais US

            localdub
            localdub setup
            localdub doctor
            localdub voices audition [--voice michael-us] [--reference voix.wav] [--text "..."]
            localdub dub --input video.mp4
            localdub dub --input video.mp4 --audio-mode separate --voice michael-us --glossary ai --yes

            Modes audio :
              separate      retire la voix estimée, conserve l'accompagnement, puis ajoute le doublage
              duck          atténue automatiquement la piste originale pendant le doublage
              external-mix  crée une nouvelle vidéo avec la voix anglaise seule et conserve le WAV pour Resolve

            Production :
              --production video  produit la vidéo traduite, le WAV, le SRT et le JSON (défaut)
              --production wav    produit le WAV traduit, le SRT et le JSON sans reconstruire la vidéo

            Options :
              --model NAME       modèle Ollama (défaut : appsettings.json)
              --preserve LISTE   termes inchangés, séparés par virgules
              --voice ID         michael-us (défaut), adam-us ou narrator-us
              --reference PATH   référence vocale WAV propre de 6 à 10 secondes
              --voice-variant V  neutral, stable ou expressive
              --overwrite        autorise le remplacement d'une précédente sortie -EN, jamais de la source
              --yes              mode entièrement non interactif
            """);
    }
}
