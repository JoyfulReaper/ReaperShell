using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class PrayCommand : IShellCommand
{
    private const string Usage = """
Usage:
  pray
  pray status
  pray hard
""";

    private readonly ShellCurseState _curseState;

    public PrayCommand(ShellCurseState curseState)
    {
        _curseState = curseState;
    }

    public string Name => "pray";

    public string Description => "Prints a pseudo-ritual shell response and may add blessing charges.";

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
            WriteStatus(context);
            return Task.FromResult(0);
        }

        var hard = IsHardCommand(args);
        var result = _curseState.Pray(hard);
        context.WriteLine(result.Message);
        return Task.FromResult(0);
    }

    private static bool IsSupportedArgument(string argument)
    {
        return string.Equals(argument, "status", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "hard", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStatusCommand(IReadOnlyList<string> args)
    {
        return args.Count == 1 && string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHardCommand(IReadOnlyList<string> args)
    {
        return args.Count == 1 && string.Equals(args[0], "hard", StringComparison.OrdinalIgnoreCase);
    }

    private void WriteStatus(ShellContext context)
    {
        foreach (var line in _curseState.GetStatusLines("Cursed mode status"))
        {
            context.WriteLine(line);
        }
    }
}
