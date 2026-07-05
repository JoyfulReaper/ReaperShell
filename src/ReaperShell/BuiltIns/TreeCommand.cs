using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class TreeCommand : IShellCommand
{
    public string Name => "tree";

    public string Description => "Prints a directory tree.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseArgs(context, args, out var rootPath, out var directoriesOnly))
        {
            return Task.FromResult(1);
        }

        if (!Directory.Exists(rootPath))
        {
            context.WriteErrorLine($"Directory does not exist: {rootPath}");
            return Task.FromResult(1);
        }

        var visited = new HashSet<string>(PathComparisonHelper.FileSystemComparer);
        WriteTree(context, rootPath, prefix: string.Empty, directoriesOnly, visited, isRoot: true, isLast: true);
        return Task.FromResult(0);
    }

    private static bool TryParseArgs(
        ShellContext context,
        IReadOnlyList<string> args,
        out string rootPath,
        out bool directoriesOnly)
    {
        rootPath = context.WorkingDirectory.FullName;
        directoriesOnly = false;

        if (args.Count == 0)
        {
            return true;
        }

        if (args.Count == 1)
        {
            if (IsOption(args[0]))
            {
                directoriesOnly = IsDirectoriesOnly(args[0]);
                return true;
            }

            rootPath = ResolvePath(context, args[0]);
            return true;
        }

        if (args.Count == 2 && !IsOption(args[0]) && IsOption(args[1]))
        {
            rootPath = ResolvePath(context, args[0]);
            directoriesOnly = IsDirectoriesOnly(args[1]);
            return true;
        }

        if (args.Count == 2 && IsOption(args[0]) && !IsOption(args[1]))
        {
            directoriesOnly = IsDirectoriesOnly(args[0]);
            rootPath = ResolvePath(context, args[1]);
            return true;
        }

        context.WriteErrorLine("Usage: tree [path] [-d|--directories-only]");
        return false;
    }

    private static bool IsOption(string value)
    {
        return string.Equals(value, "-d", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "--directories-only", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectoriesOnly(string value)
    {
        return string.Equals(value, "-d", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "--directories-only", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePath(ShellContext context, string path)
    {
        return Path.GetFullPath(path, context.WorkingDirectory.FullName);
    }

    private static void WriteTree(
        ShellContext context,
        string path,
        string prefix,
        bool directoriesOnly,
        HashSet<string> visited,
        bool isRoot,
        bool isLast)
    {
        var directoryInfo = new DirectoryInfo(path);
        var displayName = string.IsNullOrWhiteSpace(directoryInfo.Name) ? path : directoryInfo.Name;
        var branch = isRoot ? string.Empty : (isLast ? "└── " : "├── ");
        context.WriteLine(prefix + branch + displayName);

        if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            context.WriteLine(prefix + "    [reparse point]");
            return;
        }

        if (!visited.Add(directoryInfo.FullName))
        {
            context.WriteLine(prefix + "    [cycle]");
            return;
        }

        IEnumerable<FileSystemInfo> children;
        try
        {
            children = directoryInfo.EnumerateFileSystemInfos()
                .Where(entry => !directoriesOnly || entry.Attributes.HasFlag(FileAttributes.Directory))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            context.WriteLine(prefix + "    [inaccessible: " + ex.Message + "]");
            return;
        }

        var items = children.ToArray();
        for (var index = 0; index < items.Length; index++)
        {
            var child = items[index];
            var childIsLast = index == items.Length - 1;
            var childPrefix = prefix + (isRoot ? string.Empty : (isLast ? "    " : "│   "));

            if (child is DirectoryInfo childDirectory)
            {
                try
                {
                    WriteTree(context, childDirectory.FullName, childPrefix, directoriesOnly, visited, isRoot: false, childIsLast);
                }
                catch (Exception ex)
                {
                    context.WriteLine(childPrefix + (childIsLast ? "└── " : "├── ") + child.Name + " [inaccessible: " + ex.Message + "]");
                }
            }
            else if (!directoriesOnly)
            {
                context.WriteLine(childPrefix + (childIsLast ? "└── " : "├── ") + child.Name);
            }
        }
    }
}
