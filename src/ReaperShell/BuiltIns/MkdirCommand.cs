using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class MkdirCommand : IShellCommand
{
    public string Name => "mkdir";

    public string Description => "Creates one or more directories.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            context.WriteErrorLine("Usage: mkdir <path> [path...]");
            return Task.FromResult(1);
        }

        var hadError = false;
        foreach (var arg in args)
        {
            var directoryPath = FileSystemCommandHelpers.ResolvePath(context, arg);
            try
            {
                Directory.CreateDirectory(directoryPath);
            }
            catch (Exception ex)
            {
                hadError = true;
                context.WriteErrorLine($"Failed to create directory '{directoryPath}': {ex.Message}");
            }
        }

        return Task.FromResult(hadError ? 1 : 0);
    }
}
