using LocalDub.Models;
using LocalDub.Services;
using LocalDub.Utils;
using LocalDub.Configuration;

namespace LocalDub.Cli;

public sealed class InteractiveConsole(
    VoiceProfileService voices,
    AppSettings settings,
    ProcessRunner processRunner)
{
    public async Task<DubOptions> BuildDubOptionsAsync(CliArguments arguments, CancellationToken cancellationToken)
    {
        var nonInteractive = arguments.Has("yes");
        if (!nonInteractive)
        {
            ShowLaunchScreen();
        }

        var input = arguments.Get("input");
        if (string.IsNullOrWhiteSpace(input))
        {
            if (nonInteractive)
            {
                throw new ArgumentException("--input est obligatoire avec --yes.");
            }
            input = Ask("Chemin de la vidéo source");
        }
        input = input.Trim().Trim('"');

        var productionMode = arguments.Has("production")
            ? arguments.GetProductionMode()
            : nonInteractive ? ProductionMode.CompleteVideo : AskProductionMode();
        var mode = arguments.Has("audio-mode")
            ? arguments.GetAudioMode()
            : nonInteractive ? AudioMode.Separate : AskAudioMode();
        var voice = arguments.Get("voice");
        if (string.IsNullOrWhiteSpace(voice) && !nonInteractive)
        {
            voice = await AskVoiceAsync(cancellationToken);
        }

        var reference = arguments.Get("reference");
        var voiceVariant = arguments.Get("voice-variant") ?? "neutral";
        if (!nonInteractive)
        {
            reference ??= await AskReferenceVoiceAsync(voice ?? "michael-us", cancellationToken);
            Console.WriteLine("Variante vocale : 1=neutral, 2=stable, 3=expressive");
            voiceVariant = Ask("Choix", "1") switch
            {
                "1" => "neutral",
                "2" => "stable",
                "3" => "expressive",
                _ => throw new ArgumentException("Variante vocale invalide.")
            };
        }

        var glossary = arguments.Get("glossary");
        if (glossary is null && !nonInteractive)
        {
            glossary = AskGlossary();
        }

        var preservedTerms = ParsePreservedTerms(arguments.Get("preserve"));
        if (!nonInteractive && preservedTerms.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Termes à conserver exactement (sigles, produits, marques).");
            Console.WriteLine("Séparez-les par une virgule ; laissez vide pour utiliser seulement le glossaire.");
            preservedTerms = ParsePreservedTerms(
                Ask("Exemple : SRP, SOLID, DRY, MAF, Semantic Kernel", string.Empty));
        }

        var ollamaModel = arguments.Get("model");
        if (string.IsNullOrWhiteSpace(ollamaModel) && !nonInteractive)
        {
            ollamaModel = await AskModelAsync(cancellationToken);
        }

        var overwrite = arguments.Has("overwrite");
        var outputPath = GetPrimaryOutputPath(input, productionMode);
        if (!nonInteractive && File.Exists(outputPath))
        {
            Console.WriteLine();
            Console.WriteLine($"Une sortie existe déjà : {outputPath}");
            overwrite = Confirm("La remplacer ?");
            if (!overwrite)
            {
                throw new OperationCanceledException("Sortie conservée.");
            }
        }

        var options = new DubOptions
        {
            InputPath = input,
            AudioMode = mode,
            ProductionMode = productionMode,
            VoiceProfileId = voice ?? "michael-us",
            VoiceReferencePath = string.IsNullOrWhiteSpace(reference) ? null : reference,
            VoiceVariant = voiceVariant,
            OllamaModel = ollamaModel,
            GlossaryName = string.IsNullOrWhiteSpace(glossary) ? null : glossary,
            PreservedTerms = preservedTerms,
            NonInteractive = nonInteractive,
            Overwrite = overwrite,
            KeepWorkFiles = true
        };

        if (!nonInteractive)
        {
            Console.WriteLine();
            Console.WriteLine($"Source     : {Path.GetFullPath(options.InputPath)} (lecture seule)");
            Console.WriteLine($"Production : {DescribeProductionMode(options.ProductionMode)}");
            Console.WriteLine($"Sortie     : {GetPrimaryOutputPath(options.InputPath, options.ProductionMode)}");
            Console.WriteLine($"Mode audio : {options.AudioMode}");
            Console.WriteLine($"Voix       : {options.VoiceProfileId}");
            Console.WriteLine($"Référence  : {options.VoiceReferencePath ?? "référence du profil"}");
            Console.WriteLine($"Variante   : {options.VoiceVariant}");
            Console.WriteLine($"Glossaire  : {options.GlossaryName ?? "aucun"}");
            Console.WriteLine($"Traduction : {options.OllamaModel ?? settings.Ollama.Model}");
            Console.WriteLine($"Termes     : {(options.PreservedTerms.Count == 0 ? "ceux du glossaire" : string.Join(", ", options.PreservedTerms))}");
            if (!Confirm("Lancer le doublage ?"))
            {
                throw new OperationCanceledException("Opération annulée.");
            }
        }

        return options;
    }

    private static ProductionMode AskProductionMode()
    {
        Console.WriteLine();
        Console.WriteLine("Production :");
        Console.WriteLine("  1. Production complète (vidéo traduite + WAV + sous-titres)");
        Console.WriteLine("  2. WAV traduit uniquement (pas de reconstruction de la vidéo)");
        return Ask("Choix", "1") switch
        {
            "1" => ProductionMode.CompleteVideo,
            "2" => ProductionMode.TranslatedWavOnly,
            _ => throw new ArgumentException("Choix de production invalide.")
        };
    }

    private static string GetPrimaryOutputPath(string input, ProductionMode productionMode)
    {
        var video = OutputPathPolicy.GetEnglishVideoPath(input);
        return productionMode == ProductionMode.CompleteVideo
            ? video
            : Path.ChangeExtension(video, ".wav");
    }

    private static string DescribeProductionMode(ProductionMode productionMode) =>
        productionMode == ProductionMode.CompleteVideo ? "Vidéo traduite complète" : "WAV traduit uniquement";

    private static AudioMode AskAudioMode()
    {
        Console.WriteLine();
        Console.WriteLine("Traitement de la bande-son :");
        Console.WriteLine("  1. Séparer la voix française et conserver la musique (recommandé)");
        Console.WriteLine("  2. Atténuer le son original pendant la voix anglaise");
        Console.WriteLine("  3. Créer une voix anglaise seule pour un mixage externe");
        return Ask("Choix", "1") switch
        {
            "1" => AudioMode.Separate,
            "2" => AudioMode.Duck,
            "3" => AudioMode.ExternalMix,
            _ => throw new ArgumentException("Choix audio invalide.")
        };
    }

    private async Task<string> AskVoiceAsync(CancellationToken cancellationToken)
    {
        var profiles = await voices.GetProfilesAsync(cancellationToken);
        Console.WriteLine();
        Console.WriteLine("Profils vocaux :");
        for (var index = 0; index < profiles.Count; index++)
        {
            Console.WriteLine($"  {index + 1}. {profiles[index].DisplayName} ({profiles[index].Id})");
        }
        var selected = int.Parse(Ask("Choix", "1"));
        if (selected < 1 || selected > profiles.Count)
        {
            throw new ArgumentException("Profil vocal invalide.");
        }
        return profiles[selected - 1].Id;
    }

    private async Task<string> AskReferenceVoiceAsync(string voiceProfileId, CancellationToken cancellationToken)
    {
        var profile = await voices.GetRequiredAsync(voiceProfileId, cancellationToken);
        var files = voices.GetReferenceAudioFiles();

        Console.WriteLine();
        Console.WriteLine("Référence vocale :");
        Console.WriteLine($"  1. Référence du profil ({profile.ReferenceAudio ?? "voix intégrée de Chatterbox"})");
        for (var index = 0; index < files.Count; index++)
        {
            Console.WriteLine($"  {index + 2}. {Path.GetFileName(files[index])}");
        }
        Console.WriteLine($"  {files.Count + 2}. Indiquer un autre fichier WAV");

        var selected = int.Parse(Ask("Choix", "1"));
        if (selected == 1)
        {
            return string.Empty;
        }

        if (selected >= 2 && selected < files.Count + 2)
        {
            return files[selected - 2];
        }

        if (selected == files.Count + 2)
        {
            return Ask("Chemin du fichier WAV");
        }

        throw new ArgumentException("Référence vocale invalide.");
    }

    private static string? AskGlossary()
    {
        Console.WriteLine();
        Console.WriteLine("Glossaire : 1=IA/informatique, 2=synthétiseurs, 3=aucun");
        return Ask("Choix", "1") switch
        {
            "1" => "ai",
            "2" => "synths",
            "3" => null,
            _ => throw new ArgumentException("Glossaire invalide.")
        };
    }

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
        return string.Equals(answer, "o", StringComparison.OrdinalIgnoreCase)
               || string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> AskModelAsync(CancellationToken cancellationToken)
    {
        var models = new List<string>();
        try
        {
            var result = await processRunner.RunAsync("ollama", ["list"], cancellationToken: cancellationToken);
            models = result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Where(name => !name.StartsWith("all-minilm", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(name => name.Equals(settings.Ollama.Model, StringComparison.OrdinalIgnoreCase))
                .ThenBy(name => ModelOrder(name))
                .ToList();
        }
        catch (Exception exception) when (exception is FileNotFoundException or ProcessExecutionException)
        {
            return settings.Ollama.Model;
        }

        if (models.Count == 0)
        {
            return settings.Ollama.Model;
        }

        Console.WriteLine();
        Console.WriteLine("Modèle de traduction :");
        for (var index = 0; index < models.Count; index++)
        {
            Console.WriteLine($"  {index + 1}. {models[index]}{DescribeModel(models[index])}");
        }

        var selectedText = Ask("Choix", "1");
        return int.TryParse(selectedText, out var selected) && selected >= 1 && selected <= models.Count
            ? models[selected - 1]
            : throw new ArgumentException("Modèle Ollama invalide.");
    }

    private static int ModelOrder(string name) =>
        name.StartsWith("llama3.1", StringComparison.OrdinalIgnoreCase) ? 1 :
        name.StartsWith("granite4", StringComparison.OrdinalIgnoreCase) ? 2 :
        name.StartsWith("deepseek-coder", StringComparison.OrdinalIgnoreCase) ? 4 : 3;

    private static string DescribeModel(string name) =>
        name.StartsWith("gemma4:12b", StringComparison.OrdinalIgnoreCase) ? " — meilleure qualité actuelle" :
        name.StartsWith("llama3.1", StringComparison.OrdinalIgnoreCase) ? " — plus léger, bon candidat" :
        name.StartsWith("granite4:3b", StringComparison.OrdinalIgnoreCase) ? " — très rapide, qualité plus variable" :
        name.StartsWith("deepseek-coder", StringComparison.OrdinalIgnoreCase) ? " — spécialisé code, déconseillé ici" :
        string.Empty;

    private static List<string> ParsePreservedTerms(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static void ShowLaunchScreen()
    {
        if (!Console.IsOutputRedirected)
        {
            Console.Clear();
        }

        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    LocalDub.NET                         ║");
        Console.WriteLine("║       Doublage local français → anglais US             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("Indiquez votre vidéo puis validez les choix proposés.");
        Console.WriteLine("La vidéo originale ne sera jamais modifiée.");
        Console.WriteLine();
    }
}
