namespace ReaperShell.Abstractions;

public interface IShellCommand
{
    string Name { get; }

    string Description { get; }

    Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default);
}
