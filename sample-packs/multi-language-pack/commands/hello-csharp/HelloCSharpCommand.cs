using ReaperShell.Abstractions;

namespace HelloCSharpCommand;

public sealed class HelloCSharpCommand : IShellCommand
{
    public string Name => "hello-csharp";

    public string Description => "Prints a hello message from a C# command pack.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        context.WriteLine("Hello from the C# command pack.");
        return Task.FromResult(0);
    }
}
