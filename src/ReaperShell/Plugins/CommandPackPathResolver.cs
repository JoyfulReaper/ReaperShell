using ReaperShell.Shell;

namespace ReaperShell.Plugins;

public static class CommandPackPathResolver
{
    public static StringComparison PathComparison =>
        PathComparisonHelper.FileSystemComparison;

    public static string EnsurePathWithinRoot(
        string rootPath,
        string candidatePath,
        string pathDescription)
    {
        var fullRootPath = Path.GetFullPath(rootPath);
        var fullCandidatePath = Path.GetFullPath(candidatePath);

        if (!IsPathWithinRoot(fullRootPath, fullCandidatePath))
        {
            throw new InvalidOperationException($"{pathDescription} must stay inside the command pack root.");
        }

        return fullCandidatePath;
    }

    public static string ResolveCommandsRoot(string repoRoot, string? commandsPath)
    {
        if (string.IsNullOrWhiteSpace(commandsPath))
        {
            throw new InvalidOperationException("shellpack.json commandsPath cannot be empty.");
        }

        var combinedPath = Path.IsPathRooted(commandsPath)
            ? commandsPath
            : Path.Combine(repoRoot, commandsPath);

        return EnsurePathWithinRoot(repoRoot, combinedPath, "shellpack.json commandsPath");
    }

    public static bool IsPathWithinRoot(
        string rootPath,
        string candidatePath,
        bool allowExactMatch = true)
    {
        var normalizedRootPath = AppendDirectorySeparator(Path.GetFullPath(rootPath));
        var normalizedCandidatePath = PathComparisonHelper.NormalizeFullPath(candidatePath);
        var rootWithoutSeparator = normalizedRootPath.TrimEnd(Path.DirectorySeparatorChar);

        if (allowExactMatch &&
            string.Equals(
                normalizedCandidatePath,
                rootWithoutSeparator,
                PathComparison))
        {
            return true;
        }

        return normalizedCandidatePath.StartsWith(
            normalizedRootPath,
            PathComparison);
    }

    private static string AppendDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
