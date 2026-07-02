using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class PwdCommand : IShellCommand
{
    public string Name => "pwd";

    public string Description => "Prints the current working directory.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        context.WriteLine(context.WorkingDirectory.FullName);
        return Task.FromResult(0);
    }
}
