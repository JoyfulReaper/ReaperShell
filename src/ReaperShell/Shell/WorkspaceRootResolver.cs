namespace ReaperShell.Shell;

internal static class WorkspaceRootResolver
{
    private const string SolutionFileName = "ReaperShell.slnx";
    public static string FindWorkspaceRoot()
    {
        var assemblyLocation = Path.GetDirectoryName(typeof(WorkspaceRootResolver).Assembly.Location);
        return FindWorkspaceRoot(Environment.CurrentDirectory, AppContext.BaseDirectory, assemblyLocation ?? string.Empty);
    }

    internal static string FindWorkspaceRoot(params string[] startingPoints)
    {
        foreach (var startingPoint in startingPoints)
        {
            if (TryFindWorkspaceRoot(startingPoint, out var workspaceRoot))
            {
                return workspaceRoot;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the ReaperShell workspace root. Expected a directory containing ReaperShell.slnx, src/ReaperShell.Abstractions/ReaperShell.Abstractions.csproj, or ReaperShell.Abstractions/ReaperShell.Abstractions.csproj.");
    }

    internal static string GetReaperShellAbstractionsProjectPath(string workspaceRoot)
    {
        var sourceLayoutPath = Path.GetFullPath(
            Path.Combine(
                workspaceRoot,
                "src",
                "ReaperShell.Abstractions",
                "ReaperShell.Abstractions.csproj"));

        if (File.Exists(sourceLayoutPath))
        {
            return sourceLayoutPath;
        }

        var adjacentLayoutPath = Path.GetFullPath(
            Path.Combine(
                workspaceRoot,
                "ReaperShell.Abstractions",
                "ReaperShell.Abstractions.csproj"));

        if (File.Exists(adjacentLayoutPath))
        {
            return adjacentLayoutPath;
        }

        return sourceLayoutPath;
    }

    private static bool TryFindWorkspaceRoot(string startingPoint, out string workspaceRoot)
    {
        workspaceRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(startingPoint))
        {
            return false;
        }

        var current = new DirectoryInfo(Path.GetFullPath(startingPoint));
        if (File.Exists(current.FullName))
        {
            current = current.Parent!;
        }

        while (current is not null)
        {
            if (IsWorkspaceRoot(current.FullName))
            {
                workspaceRoot = current.FullName;
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool IsWorkspaceRoot(string candidateDirectory)
    {
        return IsSourceWorkspaceRoot(candidateDirectory) ||
               IsAdjacentPublishedWorkspaceRoot(candidateDirectory);
    }

    private static bool IsSourceWorkspaceRoot(string candidateDirectory)
    {
        return File.Exists(Path.Combine(candidateDirectory, SolutionFileName)) ||
               File.Exists(Path.Combine(
                   candidateDirectory,
                   "src",
                   "ReaperShell.Abstractions",
                   "ReaperShell.Abstractions.csproj"));
    }

    private static bool IsAdjacentPublishedWorkspaceRoot(string candidateDirectory)
    {
        return HasReaperShellAppMarker(candidateDirectory) &&
               File.Exists(Path.Combine(
                   candidateDirectory,
                   "ReaperShell.Abstractions",
                   "ReaperShell.Abstractions.csproj"));
    }

    private static bool HasReaperShellAppMarker(string candidateDirectory)
    {
        return File.Exists(Path.Combine(candidateDirectory, "ReaperShell.exe")) ||
               File.Exists(Path.Combine(candidateDirectory, "ReaperShell.dll")) ||
               File.Exists(Path.Combine(candidateDirectory, "ReaperShell"));
    }
}
