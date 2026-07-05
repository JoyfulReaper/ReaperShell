using ReaperShell.Shell;

namespace ReaperShell.Plugins;

public static class CommandProjectDiscovery
{
    private static readonly string[] SupportedProjectPatterns = ["*.csproj", "*.fsproj", "*.vbproj"];

    public static List<string> DiscoverProjects(string commandsRoot)
    {
        if (!Directory.Exists(commandsRoot))
        {
            return [];
        }

        return SupportedProjectPatterns
            .SelectMany(pattern => Directory.GetFiles(commandsRoot, pattern, SearchOption.AllDirectories))
            .Select(path => CommandPackPathResolver.EnsurePathWithinRoot(commandsRoot, path, "Command project path"))
            .Distinct(PathComparisonHelper.FileSystemComparer)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
