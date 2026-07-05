using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class HistoryCommand : IShellCommand
{
    private readonly ShellSessionState _sessionState;

    public HistoryCommand(ShellSessionState sessionState)
    {
        _sessionState = sessionState;
    }

    public string Name => "history";

    public string Description => "Prints or clears commands from the current shell session.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            return Task.FromResult(PrintHistory(context));
        }

        if (args.Count == 1 && string.Equals(args[0], "clear", StringComparison.OrdinalIgnoreCase))
        {
            _sessionState.ClearHistory();
            context.WriteLine("History cleared.");
            return Task.FromResult(0);
        }

        context.WriteErrorLine("Usage: history [clear]");
        return Task.FromResult(1);
    }

    private int PrintHistory(ShellContext context)
    {
        var history = _sessionState.GetHistory();
        if (history.Count == 0)
        {
            context.WriteLine("No history recorded.");
            return 0;
        }

        for (var index = 0; index < history.Count; index++)
        {
            context.WriteLine($"{index + 1}: {history[index]}");
        }

        return 0;
    }
}
