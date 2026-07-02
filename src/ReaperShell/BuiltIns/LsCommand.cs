using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class LsCommand : IShellCommand
{
    public string Name => "ls";

    public string Description => "Lists files and directories.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var targetPath = args.Count > 0
            ? ResolvePath(context.WorkingDirectory.FullName, args[0])
            : context.WorkingDirectory.FullName;

        if (!Directory.Exists(targetPath) && !File.Exists(targetPath))
        {
            context.WriteErrorLine($"Path does not exist: {targetPath}");
            return Task.FromResult(1);
        }

        if (File.Exists(targetPath))
        {
            context.WriteLine(Path.GetFileName(targetPath));
            return Task.FromResult(0);
        }

        var directory = new DirectoryInfo(targetPath);
        foreach (var entry in directory.EnumerateFileSystemInfos().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var marker = entry.Attributes.HasFlag(FileAttributes.Directory) ? "<DIR>" : "     ";
            context.WriteLine($"{marker} {entry.Name}");
        }

        return Task.FromResult(0);
    }

    private static string ResolvePath(string basePath, string inputPath)
    {
        return Path.GetFullPath(inputPath, basePath);
    }
}
