using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class CpCommand : IShellCommand
{
    public string Name => "cp";

    public string Description => "Copies files and directories.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count < 2)
        {
            context.WriteErrorLine("Usage: cp [-r|--recursive] <source> <destination>");
            return Task.FromResult(1);
        }

        var recursive = false;
        var positionals = new List<string>();

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "-r":
                case "--recursive":
                    recursive = true;
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal) && arg is not "-")
                    {
                        context.WriteErrorLine($"Unknown option: {arg}");
                        return Task.FromResult(1);
                    }

                    positionals.Add(arg);
                    break;
            }
        }

        if (positionals.Count != 2)
        {
            context.WriteErrorLine("Usage: cp [-r|--recursive] <source> <destination>");
            return Task.FromResult(1);
        }

        var sourcePath = FileSystemCommandHelpers.ResolvePath(context, positionals[0]);
        var destinationPath = FileSystemCommandHelpers.ResolvePath(context, positionals[1]);

        try
        {
            if (FileSystemCommandHelpers.PathsEqual(sourcePath, destinationPath))
            {
                return Task.FromResult(0);
            }

            if (File.Exists(sourcePath))
            {
                return Task.FromResult(CopyFile(context, sourcePath, destinationPath));
            }

            if (Directory.Exists(sourcePath))
            {
                if (!recursive)
                {
                    context.WriteErrorLine("Directory copy requires -r/--recursive.");
                    return Task.FromResult(1);
                }

                if (FileSystemCommandHelpers.IsReparsePointDirectory(sourcePath))
                {
                    context.WriteErrorLine($"Cannot recursively copy reparse-point directory: {sourcePath}");
                    return Task.FromResult(1);
                }

                return Task.FromResult(CopyDirectory(context, sourcePath, destinationPath));
            }

            context.WriteErrorLine($"Source path does not exist: {sourcePath}");
            return Task.FromResult(1);
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Copy failed: {ex.Message}");
            return Task.FromResult(1);
        }
    }

    private static int CopyFile(ShellContext context, string sourcePath, string destinationPath)
    {
        var finalDestinationPath = FileSystemCommandHelpers.ResolveDestinationPath(sourcePath, destinationPath);
        if (File.Exists(finalDestinationPath))
        {
            File.Copy(sourcePath, finalDestinationPath, overwrite: true);
            return 0;
        }

        var finalDirectory = Path.GetDirectoryName(finalDestinationPath);
        if (!string.IsNullOrWhiteSpace(finalDirectory))
        {
            Directory.CreateDirectory(finalDirectory);
        }

        File.Copy(sourcePath, finalDestinationPath, overwrite: true);
        return 0;
    }

    private static int CopyDirectory(ShellContext context, string sourcePath, string destinationPath)
    {
        var finalDestinationPath = FileSystemCommandHelpers.ResolveDestinationPath(sourcePath, destinationPath);
        if (File.Exists(finalDestinationPath) || Directory.Exists(finalDestinationPath))
        {
            context.WriteErrorLine($"Destination path already exists: {finalDestinationPath}");
            return 1;
        }

        if (FileSystemCommandHelpers.IsSameOrSubPath(sourcePath, finalDestinationPath))
        {
            context.WriteErrorLine("Cannot copy a directory into itself.");
            return 1;
        }

        FileSystemCommandHelpers.CopyDirectoryRecursive(sourcePath, finalDestinationPath, overwriteFiles: true);
        return 0;
    }
}
