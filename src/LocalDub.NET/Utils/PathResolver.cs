namespace LocalDub.Utils;

/// <summary>
/// Resolves relative paths against the LocalDub.NET project root, and locates that root by
/// walking up from the current directory in search of appsettings.json.
/// </summary>
public sealed class PathResolver(string projectRoot)
{
    /// <summary>Absolute path of the resolved project root.</summary>
    public string ProjectRoot { get; } = Path.GetFullPath(projectRoot);

    /// <summary>
    /// Returns <paramref name="path"/> unchanged (as an absolute path) if it is already rooted,
    /// otherwise resolves it relative to <see cref="ProjectRoot"/>.
    /// </summary>
    public string Resolve(string path) => Path.IsPathRooted(path)
        ? Path.GetFullPath(path)
        : Path.GetFullPath(Path.Combine(ProjectRoot, path));

    /// <summary>
    /// Walks up from the current working directory until it finds a folder containing
    /// appsettings.json, falling back to the application base directory if none is found.
    /// </summary>
    public static string FindProjectRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "appsettings.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
