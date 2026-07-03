using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class PrayCommand : IShellCommand
{
    private static readonly string[] Responses =
    [
        "THE HEAP ACCEPTS YOUR OFFERING.",
        "CSS DEMONS REMAIN CONTAINED.",
        "THE COLLECTIBLE ALC MAY OR MAY NOT HAVE UNLOADED.",
        "THE BUILD SPIRITS REQUEST ONE MORE CLEAN RELOAD."
    ];

    public string Name => "pray";

    public string Description => "Prints a pseudo-ritual shell response.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        context.WriteLine(Responses[Random.Shared.Next(Responses.Length)]);
        return Task.FromResult(0);
    }
}
