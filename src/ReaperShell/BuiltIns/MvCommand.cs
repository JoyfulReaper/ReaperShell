using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class MvCommand : IShellCommand
{
    public string Name => "mv";

    public string Description => "Moves or renames files and directories.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count != 2)
        {
            context.WriteErrorLine("Usage: mv <source> <destination>");
            return Task.FromResult(1);
        }

        var sourcePath = FileSystemCommandHelpers.ResolvePath(context, args[0]);
        var destinationPath = FileSystemCommandHelpers.ResolvePath(context, args[1]);

        try
        {
            if (FileSystemCommandHelpers.PathsEqual(sourcePath, destinationPath))
            {
                return Task.FromResult(0);
            }

            if (File.Exists(sourcePath))
            {
                return Task.FromResult(MoveFile(context, sourcePath, destinationPath));
            }

            if (Directory.Exists(sourcePath))
            {
                return Task.FromResult(MoveDirectory(context, sourcePath, destinationPath));
            }

            context.WriteErrorLine($"Source path does not exist: {sourcePath}");
            return Task.FromResult(1);
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Move failed: {ex.Message}");
            return Task.FromResult(1);
        }
    }

    private static int MoveFile(ShellContext context, string sourcePath, string destinationPath)
    {
        var finalDestinationPath = FileSystemCommandHelpers.ResolveDestinationPath(sourcePath, destinationPath);
        if (FileSystemCommandHelpers.PathsEqual(sourcePath, finalDestinationPath))
        {
            return 0;
        }

        var finalDirectory = Path.GetDirectoryName(finalDestinationPath);
        if (!string.IsNullOrWhiteSpace(finalDirectory))
        {
            Directory.CreateDirectory(finalDirectory);
        }

        File.Move(sourcePath, finalDestinationPath, overwrite: true);
        return 0;
    }

    private static int MoveDirectory(ShellContext context, string sourcePath, string destinationPath)
    {
        var finalDestinationPath = FileSystemCommandHelpers.ResolveDestinationPath(sourcePath, destinationPath);
        if (File.Exists(finalDestinationPath) || Directory.Exists(finalDestinationPath))
        {
            context.WriteErrorLine($"Destination path already exists: {finalDestinationPath}");
            return 1;
        }

        if (FileSystemCommandHelpers.IsSameOrSubPath(sourcePath, finalDestinationPath))
        {
            context.WriteErrorLine("Cannot move a directory into itself.");
            return 1;
        }

        var finalParentDirectory = Path.GetDirectoryName(finalDestinationPath);
        if (!string.IsNullOrWhiteSpace(finalParentDirectory))
        {
            Directory.CreateDirectory(finalParentDirectory);
        }

        if (FileSystemCommandHelpers.PathsEqual(sourcePath, finalDestinationPath))
        {
            return 0;
        }

        Directory.Move(sourcePath, finalDestinationPath);
        return 0;
    }
}
