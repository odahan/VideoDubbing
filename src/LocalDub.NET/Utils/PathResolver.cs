namespace LocalDub.Utils;

public sealed class PathResolver(string projectRoot)
{
    public string ProjectRoot { get; } = Path.GetFullPath(projectRoot);

    public string Resolve(string path) => Path.IsPathRooted(path)
        ? Path.GetFullPath(path)
        : Path.GetFullPath(Path.Combine(ProjectRoot, path));

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
