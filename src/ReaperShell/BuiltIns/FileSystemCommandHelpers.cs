using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

internal static class FileSystemCommandHelpers
{
    public static string ResolvePath(ShellContext context, string path)
    {
        return Path.GetFullPath(path, context.WorkingDirectory.FullName);
    }

    public static string ResolveDestinationPath(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(destinationPath))
        {
            return Path.Combine(destinationPath, Path.GetFileName(sourcePath));
        }

        return destinationPath;
    }

    public static bool IsSameOrSubPath(string parentPath, string candidatePath)
    {
        var normalizedParent = PathComparisonHelper.NormalizeFullPath(parentPath);
        var normalizedCandidate = PathComparisonHelper.NormalizeFullPath(candidatePath);
        if (PathComparisonHelper.PathsEqual(normalizedParent, normalizedCandidate))
        {
            return true;
        }

        var parentWithSeparator = normalizedParent.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedParent
            : normalizedParent + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(parentWithSeparator, PathComparisonHelper.FileSystemComparison);
    }

    public static bool PathsEqual(string leftPath, string rightPath)
    {
        return PathComparisonHelper.PathsEqual(leftPath, rightPath);
    }

    public static void CopyDirectoryRecursive(string sourceDirectory, string destinationDirectory, bool overwriteFiles)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var childDirectory in Directory.GetDirectories(sourceDirectory))
        {
            var childDestination = Path.Combine(destinationDirectory, Path.GetFileName(childDirectory));
            CopyDirectoryRecursive(childDirectory, childDestination, overwriteFiles);
        }

        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            var fileDestination = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, fileDestination, overwriteFiles);
        }
    }
}
