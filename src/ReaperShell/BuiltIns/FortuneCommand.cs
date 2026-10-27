using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class FortuneCommand : IShellCommand
{
    private const string Usage = """
Usage:
  fortune
  fortune read
  fortune status
""";

    private readonly ShellCurseState _curseState;

    public FortuneCommand(ShellCurseState curseState)
    {
        _curseState = curseState;
    }

    public string Name => "fortune";

    public string Description => "Prints a small shell fortune and may reveal an omen.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count > 1 || (args.Count == 1 && !IsSupportedArgument(args[0])))
        {
            context.WriteErrorLine(Usage);
            return Task.FromResult(1);
        }

        if (IsStatusCommand(args))
        {
            foreach (var line in _curseState.GetStatusLines("Cursed mode status"))
            {
                context.WriteLine(line);
            }

            return Task.FromResult(0);
        }

        var result = _curseState.RevealFortune();
        context.WriteLine(result.FortuneText);
        context.WriteLine(_curseState.Enabled
            ? result.OmenMessage
            : "No curse is active. The omen is purely decorative.");
        return Task.FromResult(0);
    }

    private static bool IsSupportedArgument(string argument)
    {
        return string.Equals(argument, "read", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "status", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStatusCommand(IReadOnlyList<string> args)
    {
        return args.Count == 1 && string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase);
    }
}
