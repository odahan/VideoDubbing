namespace LocalDub.Utils;

public static class OutputPathPolicy
{
    public static string GetEnglishVideoPath(string inputPath)
    {
        var fullInputPath = Path.GetFullPath(inputPath);
        var directory = Path.GetDirectoryName(fullInputPath) ?? Environment.CurrentDirectory;
        var extension = Path.GetExtension(fullInputPath);
        var name = Path.GetFileNameWithoutExtension(fullInputPath);
        return Path.Combine(directory, $"{name}-EN{extension}");
    }

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
