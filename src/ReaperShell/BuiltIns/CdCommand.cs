using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class CdCommand : IShellCommand
{
    public string Name => "cd";

    public string Description => "Changes the current working directory.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: cd <path>");
            return Task.FromResult(1);
        }

        var targetPath = Path.GetFullPath(args[0], context.WorkingDirectory.FullName);
        if (!Directory.Exists(targetPath))
        {
            context.WriteErrorLine($"Directory does not exist: {targetPath}");
            return Task.FromResult(1);
        }

        context.WorkingDirectory = new DirectoryInfo(targetPath);
        return Task.FromResult(0);
    }
}
