using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class EchoCommand : IShellCommand
{
    public string Name => "echo";

    public string Description => "Prints its arguments joined by spaces.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        context.WriteLine(string.Join(" ", args));
        return Task.FromResult(0);
    }
}
