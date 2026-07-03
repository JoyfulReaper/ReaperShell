namespace ReaperShell.Plugins;

public static class CommandPackPathResolver
{
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

    private static bool IsPathWithinRoot(string rootPath, string candidatePath)
    {
        var normalizedRootPath = AppendDirectorySeparator(Path.GetFullPath(rootPath));
        var normalizedCandidatePath = Path.GetFullPath(candidatePath);

        return string.Equals(
                   normalizedCandidatePath,
                   normalizedRootPath.TrimEnd(Path.DirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidatePath.StartsWith(
                   normalizedRootPath,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string AppendDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
