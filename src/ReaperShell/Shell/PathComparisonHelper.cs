namespace ReaperShell.Shell;

public static class PathComparisonHelper
{
    public static StringComparison FileSystemComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer FileSystemComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string NormalizeFullPath(string path)
    {
        return Path.GetFullPath(path);
    }

    public static bool PathsEqual(string leftPath, string rightPath)
    {
        return string.Equals(
            NormalizeFullPath(leftPath),
            NormalizeFullPath(rightPath),
            FileSystemComparison);
    }
}
