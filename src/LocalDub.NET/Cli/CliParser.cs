namespace LocalDub.Cli;

public static class CliParser
{
    private static readonly HashSet<string> Switches = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes", "overwrite", "help", "keep-work"
    };

    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new CliArguments { Command = "dub" };
        }

        var command = args[0].StartsWith('-') ? "dub" : args[0].ToLowerInvariant();
        var start = args[0].StartsWith('-') ? 0 : 1;
        if (command == "voices" && args.Count > 1 && args[1].Equals("audition", StringComparison.OrdinalIgnoreCase))
        {
            command = "voices-audition";
            start = 2;
        }
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var index = start; index < args.Count; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Argument inattendu : {token}");
            }

            var name = token[2..];
            if (Switches.Contains(name))
            {
                options[name] = null;
                continue;
            }

            if (++index >= args.Count || args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Une valeur est attendue après --{name}.");
            }

            options[name] = args[index];
        }

        return new CliArguments { Command = command, Options = options };
    }
}
