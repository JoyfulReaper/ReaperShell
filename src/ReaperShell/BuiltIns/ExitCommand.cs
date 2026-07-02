using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class ExitCommand : IShellCommand
{
    private readonly ShellLifetime _lifetime;

    public ExitCommand(string name, ShellLifetime lifetime)
    {
        Name = name;
        _lifetime = lifetime;
    }

    public string Name { get; }

    public string Description => "Exits the shell.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        _lifetime.RequestExit();
        return Task.FromResult(0);
    }
}
