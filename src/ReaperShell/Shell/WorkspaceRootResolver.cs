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
            "Could not locate the ReaperShell workspace root. Expected a directory containing ReaperShell.slnx or src/ReaperShell.Abstractions/ReaperShell.Abstractions.csproj.");
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
        return File.Exists(Path.Combine(candidateDirectory, SolutionFileName)) ||
               File.Exists(Path.Combine(
                   candidateDirectory,
                   "src",
                   "ReaperShell.Abstractions",
                   "ReaperShell.Abstractions.csproj"));
    }
}
