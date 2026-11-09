using LocalDub.Cli;
using LocalDub.Configuration;
using LocalDub.OpenAI;
using LocalDub.Services;

namespace LocalDub;

/// <summary>Offers account, model and dubbing actions from the same console session.</summary>
internal static class ConsoleMenu
{
    /// <summary>Prompts for a connection and keeps the current model selection until the menu exits.</summary>
    public static async Task<int> RunAsync(
        OpenAiAccountSession account, VoiceProfileService voices, string glossaryDirectory, OpenAiSettings settings,
        Func<CliArguments, IReadOnlyList<OpenAiAccountModel>?, Task<int>> execute, CancellationToken cancellationToken)
    {
        Console.WriteLine($"{Program.EditionName} | French to US English dubbing");
        string? translationModel = null;
        var timingModel = settings.TimingModel;
        if (account.StatusKey != "OpenAiConnected")
        {
            var connect = Prompt("Connect your ChatGPT account now? [Y/n]");
            if (connect is null)
            {
                return 0;
            }
            if (connect is "" or "y" or "Y")
            {
                await TryActionAsync(() => execute(new CliArguments { Command = "sign-in" }, null), cancellationToken);
            }
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var connected = account.StatusKey == "OpenAiConnected";
            Console.WriteLine(connected ? "\nChatGPT plan usage enabled." : "\nChatGPT account not connected.");
            if (connected)
            {
                Console.WriteLine($"Translation: {translationModel ?? "automatic"} | Timing: {timingModel ?? "same model"}");
            }
            Console.WriteLine("""
                1. Dub a video
                2. Choose models
                3. Connect to ChatGPT
                4. Reset ChatGPT connection
                5. Check shared audio installation
                0. Exit
                """);

            switch (Prompt("Choose an action"))
            {
                case null or "0":
                    return 0;
                case "1" or "2" when !connected:
                    Console.WriteLine("Connect to ChatGPT first (option 3).");
                    break;
                case "1":
                    await TryActionAsync(async () =>
                    {
                        var models = await LoadModelsAsync(account, cancellationToken);
                        translationModel = SelectDefault(models, translationModel ?? settings.Model);
                        if (timingModel is not null && !models.Any(model => model.Slug == timingModel))
                        {
                            Console.WriteLine("The timing model is no longer available. Using the translation model.");
                            timingModel = null;
                        }
                        Console.WriteLine($"Using {translationModel} for translation and {timingModel ?? translationModel} for timing.");
                        var command = await ReadDubArgumentsAsync(voices, glossaryDirectory, translationModel, timingModel, cancellationToken);
                        if (command is not null)
                        {
                            await execute(command, models);
                        }
                    }, cancellationToken);
                    break;
                case "2":
                    await TryActionAsync(async () =>
                    {
                        var models = await LoadModelsAsync(account, cancellationToken);
                        for (var index = 0; index < models.Count; index++)
                        {
                            Console.WriteLine($"{index + 1}. {models[index].DisplayName} ({models[index].Slug})");
                        }
                        var defaultModel = SelectDefault(models, translationModel ?? settings.Model);
                        var selected = ReadModel(models, Prompt($"Translation model [{defaultModel}]"), defaultModel);
                        var selectedTiming = ReadModel(models, Prompt("Timing model [same as translation]"), null);
                        translationModel = selected;
                        timingModel = selectedTiming;
                    }, cancellationToken);
                    break;
                case "3":
                    await TryActionAsync(() => execute(new CliArguments { Command = "sign-in" }, null), cancellationToken);
                    break;
                case "4":
                    if (Prompt("Remove the saved ChatGPT connection? [y/N]") is "y" or "Y")
                    {
                        await TryActionAsync(() => execute(new CliArguments { Command = "sign-out" }, null), cancellationToken);
                        translationModel = null;
                        timingModel = settings.TimingModel;
                    }
                    break;
                case "5":
                    await TryActionAsync(() => execute(new CliArguments { Command = "doctor" }, null), cancellationToken);
                    break;
                default:
                    Console.WriteLine("Choose a number from the menu.");
                    break;
            }
        }
        return 2;
    }

    private static async Task<IReadOnlyList<OpenAiAccountModel>> LoadModelsAsync(
        OpenAiAccountSession account, CancellationToken cancellationToken)
    {
        using var sessionLock = OpenAiAccountSession.LockUserSession();
        var models = await account.GetModelsAsync(cancellationToken);
        return models.Count > 0 ? models : throw new InvalidOperationException("This ChatGPT account did not offer any models.");
    }

    private static string SelectDefault(IReadOnlyList<OpenAiAccountModel> models, string preferred) =>
        models.FirstOrDefault(model => model.Slug == preferred)?.Slug ?? models[0].Slug;

    private static string? ReadModel(IReadOnlyList<OpenAiAccountModel> models, string? choice, string? fallback)
    {
        if (string.IsNullOrEmpty(choice))
        {
            return fallback;
        }
        if (int.TryParse(choice, out var index) && index >= 1 && index <= models.Count)
        {
            return models[index - 1].Slug;
        }
        return models.FirstOrDefault(model => model.Slug == choice)?.Slug
            ?? throw new ArgumentException("Choose a displayed model number or slug.");
    }

    private static async Task<CliArguments?> ReadDubArgumentsAsync(
        VoiceProfileService voices, string glossaryDirectory, string model, string? timingModel, CancellationToken cancellationToken)
    {
        var input = Prompt("Video path (empty to return)");
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }
        var arguments = new CliArguments { Command = "dub" };
        arguments.Options["input"] = input.Trim('"');
        arguments.Options["model"] = model;
        arguments.Options["timing-model"] = timingModel ?? model;
        SetChoice("production", "Output", [
            ("video", "Complete video, WAV and subtitles"),
            ("wav", "Dubbed WAV and subtitles")
        ]);
        SetChoice("audio-mode", "Audio mode", [
            ("separate", "Separate the original voice and keep the accompaniment"),
            ("duck", "Lower the original soundtrack under the dub"),
            ("external-mix", "English voice only for mixing in an editor")
        ]);
        ReadOption("base-speed", "Base narration speed [1.00; enter 1.10 for +10%]");
        var profiles = await voices.GetProfilesAsync(cancellationToken);
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("No voice profiles are available.");
        }
        var voiceChoices = profiles.Select(profile => ((string?)profile.Id, $"{profile.DisplayName} ({profile.Id})")).ToArray();
        var defaultVoice = Array.FindIndex(voiceChoices, choice => choice.Item1 == "michael-us");
        SetChoice("voice", "Voice profile", voiceChoices, Math.Max(0, defaultVoice));

        var referenceChoices = new List<(string? Value, string Label)> { (null, "Use the profile's reference") };
        referenceChoices.AddRange(voices.GetReferenceAudioFiles().Select(file => ((string?)file, Path.GetFileName(file))));
        referenceChoices.Add(("", "Enter another WAV path"));
        SetChoice("reference", "Voice reference", referenceChoices, customPathPrompt: "Voice reference WAV path");
        SetChoice("voice-variant", "Voice variant", VoiceProfileService.StandardVariants
            .Select(variant => ((string?)variant, variant)).ToArray());

        var glossaryChoices = new List<(string? Value, string Label)> { (null, "No glossary") };
        if (Directory.Exists(glossaryDirectory))
        {
            var glossaries = Directory.EnumerateFiles(glossaryDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
            glossaryChoices.AddRange(glossaries.Select(name => (name, name!)));
        }
        glossaryChoices.Add(("", "Enter another glossary path"));
        SetChoice("glossary", "Glossary", glossaryChoices, customPathPrompt: "Glossary JSON path");
        ReadOption("preserve", "Terms to preserve, separated by commas [none]");
        ReadOption("transcript", "Existing LocalDub JSON for comparison [transcribe the video]", path: true);
        if (Prompt("Replace existing OpenAI exports? [y/N]") is "y" or "Y")
        {
            arguments.Options["overwrite"] = null;
        }
        return Prompt("Start dubbing and send the text to OpenAI? [y/N]") is "y" or "Y" ? arguments : null;

        void SetChoice(
            string name, string title, IReadOnlyList<(string? Value, string Label)> choices,
            int defaultIndex = 0, string? customPathPrompt = null)
        {
            var choice = ReadNumberedChoice(title, choices, defaultIndex);
            if (choice == "" && customPathPrompt is not null)
            {
                if (Prompt(customPathPrompt)?.Trim('"') is { Length: > 0 } path)
                {
                    arguments.Options[name] = Path.GetFullPath(path);
                }
            }
            else if (choice is not null)
            {
                arguments.Options[name] = choice;
            }
        }

        void ReadOption(string name, string label, bool path = false)
        {
            if (Prompt(label) is { Length: > 0 } value)
            {
                arguments.Options[name] = path ? value.Trim('"') : value;
            }
        }
    }

    private static string? ReadNumberedChoice(
        string title, IReadOnlyList<(string? Value, string Label)> choices, int defaultIndex)
    {
        Console.WriteLine(title);
        for (var index = 0; index < choices.Count; index++)
        {
            Console.WriteLine($"  {index + 1}. {choices[index].Label}");
        }
        while (true)
        {
            var input = Prompt($"Choose [{defaultIndex + 1}]");
            if (string.IsNullOrEmpty(input))
            {
                return choices[defaultIndex].Value;
            }
            if (int.TryParse(input, out var number) && number >= 1 && number <= choices.Count)
            {
                return choices[number - 1].Value;
            }
            Console.WriteLine("Choose a number from the list.");
        }
    }

    private static string? Prompt(string label)
    {
        Console.Write($"{label}: ");
        return Console.ReadLine()?.Trim();
    }

    private static async Task TryActionAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine($"Error: {Program.DescribeError(error)}");
        }
    }
}
