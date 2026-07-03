using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class FortuneCommand : IShellCommand
{
    private static readonly string[] Fortunes =
    [
        "A patient shell learns new tricks without losing its prompt.",
        "The next command pack may contain wisdom or only warnings.",
        "Trust slowly, build cleanly, reload boldly.",
        "Today is a good day to survive collectible unload uncertainty."
    ];

    public string Name => "fortune";

    public string Description => "Prints a small shell fortune.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        context.WriteLine(Fortunes[Random.Shared.Next(Fortunes.Length)]);
        return Task.FromResult(0);
    }
}
