namespace LocalDub.Utils;

/// <summary>
/// Central policy for deriving and validating output file paths, ensuring dubbed artifacts never
/// overwrite the original source video and that existing outputs are not clobbered unintentionally.
/// </summary>
public static class OutputPathPolicy
{
    /// <summary>
    /// Returns the conventional "-EN" suffixed path for the English-dubbed video derived from
    /// <paramref name="inputPath"/>, preserving its directory and extension.
    /// </summary>
    public static string GetEnglishVideoPath(string inputPath)
    {
        var fullInputPath = Path.GetFullPath(inputPath);
        var directory = Path.GetDirectoryName(fullInputPath) ?? Environment.CurrentDirectory;
        var extension = Path.GetExtension(fullInputPath);
        var name = Path.GetFileNameWithoutExtension(fullInputPath);
        return Path.Combine(directory, $"{name}-EN{extension}");
    }

    /// <summary>
    /// Validates that <paramref name="outputPath"/> is safe to write: it must not be the same file
    /// as <paramref name="inputPath"/>, and it must not already exist unless <paramref name="overwrite"/>
    /// is true.
    /// </summary>
    public static void EnsureWritableOutput(string inputPath, string outputPath, bool overwrite)
    {
        var source = Path.GetFullPath(inputPath);
        var target = Path.GetFullPath(outputPath);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Le chemin de sortie ne peut jamais être celui de la vidéo originale.");
        }

        if (File.Exists(target) && !overwrite)
        {
            throw new IOException($"Le fichier de sortie existe déjà : {target}. Utilisez --overwrite pour le remplacer.");
        }
    }
}
