using LocalDub.Configuration;
using LocalDub.Models;
using LocalDub.Services;
using LocalDub.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalDub.Voices;

public static class Program
{
    private const string AuditionText = "Welcome. This is a short voice sample for comparing stability, clarity, and expression.";

    public static async Task<int> Main()
    {
        try
        {
            var root = PathResolver.FindProjectRoot();
            var configuration = new ConfigurationBuilder()
                .SetBasePath(root)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();
            var settings = configuration.Get<AppSettings>()
                ?? throw new InvalidDataException("appsettings.json est invalide.");

            var services = new ServiceCollection();
            services.AddSingleton(settings);
            services.AddSingleton(new PathResolver(root));
            services.AddLogging(builder => builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            }).SetMinimumLevel(LogLevel.Information));
            services.AddHttpClient();
            services.AddSingleton<ProcessRunner>();
            services.AddSingleton<ToolPaths>();
            services.AddSingleton<VoiceProfileService>();
            services.AddSingleton<TtsServiceHost>();
            services.AddSingleton<ChatterboxSynthesizer>();
            services.AddSingleton<VoiceProfileCatalog>();

            await using var provider = services.BuildServiceProvider();
            await RunMenuAsync(provider, CancellationToken.None);
            return 0;
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

    private static async Task RunMenuAsync(ServiceProvider services, CancellationToken cancellationToken)
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("╔════════════════ Gestionnaire de voix LocalDub ════════════════╗");
            Console.WriteLine("║  1. Lister les profils                                        ║");
            Console.WriteLine("║  2. Ajouter une voix personnelle                              ║");
            Console.WriteLine("║  3. Générer des auditions (neutre / stable / expressive)     ║");
            Console.WriteLine("║  4. Appliquer un préréglage à un profil                       ║");
            Console.WriteLine("║  5. Supprimer un profil (le WAV est conservé)                 ║");
            Console.WriteLine("║  0. Quitter                                                  ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");

            switch (Ask("Choix"))
            {
                case "1": await ListProfilesAsync(services, cancellationToken); break;
                case "2": await AddProfileAsync(services, cancellationToken); break;
                case "3": await AuditionAsync(services, cancellationToken); break;
                case "4": await ApplyPresetAsync(services, cancellationToken); break;
                case "5": await DeleteProfileAsync(services, cancellationToken); break;
                case "0": return;
                default: Console.WriteLine("Choix invalide."); break;
            }
        }
    }

    private static async Task ListProfilesAsync(ServiceProvider services, CancellationToken cancellationToken)
    {
        var document = await services.GetRequiredService<VoiceProfileCatalog>().LoadAsync(cancellationToken);
        Console.WriteLine();
        foreach (var profile in document.Profiles)
        {
            Console.WriteLine($"- {profile.Id} — {profile.DisplayName}");
            Console.WriteLine($"  Référence : {profile.ReferenceAudio ?? "voix intégrée Chatterbox"}");
            Console.WriteLine($"  Neutre : température {profile.Temperature:0.00}, top-p {profile.TopP:0.00}, répétition {profile.RepetitionPenalty:0.00}");
        }
    }

    private static async Task AddProfileAsync(ServiceProvider services, CancellationToken cancellationToken)
    {
        var catalog = services.GetRequiredService<VoiceProfileCatalog>();
        var document = await catalog.LoadAsync(cancellationToken);
        var id = Ask("Identifiant court (ex. odile)").ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]*$"))
        {
            throw new ArgumentException("Utilisez seulement des lettres minuscules, chiffres et tirets.");
        }
        if (document.Profiles.Any(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Cet identifiant existe déjà.");
        }

        var displayName = Ask("Nom affiché", id);
        var sourceWav = Ask("Chemin du WAV de référence");
        var template = ChooseProfile(document.Profiles, "Profil de réglages à copier (Michael est conseillé)");
        var referenceAudio = catalog.CopyReferenceAudio(sourceWav, id);
        document.Profiles.Add(new VoiceProfile
        {
            Id = id,
            DisplayName = displayName,
            Engine = template.Engine,
            ReferenceAudio = referenceAudio,
            Temperature = template.Temperature,
            RepetitionPenalty = template.RepetitionPenalty,
            TopP = template.TopP,
            TopK = template.TopK,
            Description = $"Voix personnelle ajoutée depuis {Path.GetFileName(referenceAudio)}."
        });
        await catalog.SaveAsync(document, cancellationToken);
        Console.WriteLine($"Profil créé. Le WAV a été copié dans {referenceAudio}.");
        Console.WriteLine("Étape conseillée : générez maintenant les auditions, puis appliquez le préréglage préféré.");
    }

    private static async Task AuditionAsync(ServiceProvider services, CancellationToken cancellationToken)
    {
        var catalog = services.GetRequiredService<VoiceProfileCatalog>();
        var document = await catalog.LoadAsync(cancellationToken);
        var profile = ChooseProfile(document.Profiles, "Profil à écouter");
        var text = Ask("Texte anglais pour l'écoute", AuditionText);
        var output = Path.Combine(catalog.VoicesDirectory, "previews", profile.Id);
        var files = await services.GetRequiredService<ChatterboxSynthesizer>()
            .AuditionAsync(text, profile, output, cancellationToken);
        Console.WriteLine("Auditions créées :");
        foreach (var file in files)
        {
            Console.WriteLine($"  {file}");
        }
    }

    private static async Task ApplyPresetAsync(ServiceProvider services, CancellationToken cancellationToken)
    {
        var catalog = services.GetRequiredService<VoiceProfileCatalog>();
        var document = await catalog.LoadAsync(cancellationToken);
        var selected = ChooseProfile(document.Profiles, "Profil à régler");
        Console.WriteLine("1. Neutre — réglages actuels conservés");
        Console.WriteLine("2. Stable — le plus sûr pour une narration régulière");
        Console.WriteLine("3. Expressif — plus vivant, parfois moins constant");
        var preset = Ask("Choix", "1");
        var updated = preset switch
        {
            "1" => selected,
            "2" => WithTuning(selected, 0.65, 0.90, 1.25),
            "3" => WithTuning(selected, 0.95, 0.98, 1.10),
            _ => throw new ArgumentException("Préréglage invalide.")
        };
        var index = document.Profiles.FindIndex(profile => profile.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase));
        document.Profiles[index] = updated;
        await catalog.SaveAsync(document, cancellationToken);
        Console.WriteLine("Préréglage enregistré dans profiles.json.");
    }

    private static async Task DeleteProfileAsync(ServiceProvider services, CancellationToken cancellationToken)
    {
        var catalog = services.GetRequiredService<VoiceProfileCatalog>();
        var document = await catalog.LoadAsync(cancellationToken);
        var selected = ChooseProfile(document.Profiles, "Profil à supprimer");
        if (!Confirm($"Supprimer le profil {selected.Id} ? Le fichier WAV restera présent."))
        {
            return;
        }
        document.Profiles.RemoveAll(profile => profile.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase));
        await catalog.SaveAsync(document, cancellationToken);
        Console.WriteLine("Profil supprimé ; le WAV n'a pas été supprimé.");
    }

    private static VoiceProfile ChooseProfile(IReadOnlyList<VoiceProfile> profiles, string prompt)
    {
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("Aucun profil vocal n'est disponible.");
        }
        Console.WriteLine(prompt + " :");
        for (var index = 0; index < profiles.Count; index++)
        {
            Console.WriteLine($"  {index + 1}. {profiles[index].DisplayName} ({profiles[index].Id})");
        }
        var selected = int.Parse(Ask("Choix", "1"));
        if (selected < 1 || selected > profiles.Count)
        {
            throw new ArgumentException("Profil vocal invalide.");
        }
        return profiles[selected - 1];
    }

    private static VoiceProfile WithTuning(VoiceProfile profile, double temperature, double topP, double repetitionPenalty) => new()
    {
        Id = profile.Id,
        DisplayName = profile.DisplayName,
        Engine = profile.Engine,
        ReferenceAudio = profile.ReferenceAudio,
        Temperature = temperature,
        TopP = topP,
        RepetitionPenalty = repetitionPenalty,
        TopK = profile.TopK,
        Description = profile.Description
    };

    private static string Ask(string prompt, string? defaultValue = null)
    {
        Console.Write(defaultValue is null ? $"{prompt} : " : $"{prompt} [{defaultValue}] : ");
        var value = Console.ReadLine()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? defaultValue ?? string.Empty : value;
    }

    private static bool Confirm(string prompt)
    {
        Console.Write($"{prompt} [o/N] ");
        var answer = Console.ReadLine()?.Trim();
        return answer is not null && (answer.Equals("o", StringComparison.OrdinalIgnoreCase) || answer.Equals("y", StringComparison.OrdinalIgnoreCase));
    }
}
