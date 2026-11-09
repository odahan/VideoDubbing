using System.Text.RegularExpressions;

namespace LocalDub.Services;

/// <summary>Applies English pronunciations to the text sent to speech synthesis.</summary>
internal static partial class SpokenEnglish
{
    /// <summary>Spells VST as three letters while leaving the stored translation intact.</summary>
    public static string ForSynthesis(string text) => Vst().Replace(text, match =>
        match.Groups[1].Value.ToLowerInvariant() switch
        {
            "s" => "vee esstees",
            "3" => "vee esstee three",
            _ => "vee esstee"
        });

    [GeneratedRegex(@"\bVST(s|3)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Vst();
}
