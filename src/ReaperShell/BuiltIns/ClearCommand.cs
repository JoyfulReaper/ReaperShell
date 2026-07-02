using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class ClearCommand : IShellCommand
{
    public string Name => "clear";

    public string Description => "Clears the console window.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        Console.Clear();
        return Task.FromResult(0);
    }
}
