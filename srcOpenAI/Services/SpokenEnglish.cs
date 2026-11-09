using System.Text.RegularExpressions;

namespace LocalDub.Services;

/// <summary>Applies English pronunciations to the text sent to speech synthesis.</summary>
internal static partial class SpokenEnglish
{
    /// <summary>Uses explicit pronunciations for technical initialisms while leaving the translation intact.</summary>
    public static string ForSynthesis(string text)
    {
        var spoken = Vst().Replace(text, match => match.Groups[1].Value.ToLowerInvariant() switch
        {
            "s" => "vee esstees",
            "3" => "vee esstee three",
            _ => "vee esstee"
        });
        return Dj().Replace(spoken, match => match.Groups["s"].Success ? "deejays" : "deejay");
    }

    [GeneratedRegex(@"\bVST(s|3)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Vst();

    [GeneratedRegex(@"\bDJ(?<s>['’]?s)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Dj();
}
