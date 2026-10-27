using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class CurseCommand : IShellCommand
{
    private const string Usage = """
Usage:
  curse
  curse status
  curse inspect
  curse journal
  curse poke
  curse enable
  curse disable
  curse exorcise
  curse quiet
  curse listen
  curse chatter <percent>
  curse set-failure-rate <percent>
""";

    private readonly ShellCurseState _curseState;

    public CurseCommand(ShellCurseState curseState)
    {
        _curseState = curseState;
    }

    public string Name => "curse";

    public string Description => "Manages the optional cursed mode.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            WriteStatus(context);
            return Task.FromResult(0);
        }

        if (IsCommand(args, "status"))
        {
            if (args.Count != 1)
            {
                return FailUsage(context);
            }

            WriteStatus(context);
            return Task.FromResult(0);
        }

        if (IsCommand(args, "inspect"))
        {
            if (args.Count != 1)
            {
                return FailUsage(context);
            }

            WriteInspect(context);
            return Task.FromResult(0);
        }

        if (IsCommand(args, "journal"))
        {
            if (args.Count != 1)
            {
                return FailUsage(context);
            }

            WriteJournal(context);
            return Task.FromResult(0);
        }

        if (IsCommand(args, "poke"))
        {
            if (args.Count != 1)
            {
                return FailUsage(context);
            }

            context.WriteLine(_curseState.Poke());
            return Task.FromResult(0);
        }

        if (IsCommand(args, "enable"))
        {
            if (args.Count != 1)
            {
                return FailUsage(context);
            }

            _curseState.Enable();
            context.WriteLine("Cursed mode enabled. This is intentionally silly and opt-in.");
            context.WriteLine($"Mood: {_curseState.Mood}. Failure chance: {_curseState.FailureChancePercent}%.");
            return Task.FromResult(0);
        }

        if (IsCommand(args, "disable"))
        {
            if (args.Count != 1)
            {
                return FailUsage(context);
            }

            _curseState.Disable();
            context.WriteLine("The shell feels briefly less cursed. Blessing charges cleared.");
            return Task.FromResult(0);
        }

        if (IsCommand(args, "exorcise"))
        {
            if (args.Count != 1)
            {
                return FailUsage(context);
            }

            _curseState.Exorcise();
            context.WriteLine("The curse screams in YAML and leaves. Blessing charges cleared.");
            return Task.FromResult(0);
        }

        if (IsCommand(args, "quiet"))
        {
            if (args.Count != 1)
            {
                return FailUsage(context);
            }

            _curseState.Quiet();
            context.WriteLine("Ambient chatter drops to zero. The curse holds its tongue.");
            return Task.FromResult(0);
        }

        if (IsCommand(args, "listen"))
        {
            if (args.Count != 1)
            {
                return FailUsage(context);
            }

            _curseState.Listen();
            context.WriteLine("Ambient chatter restored. The shell is listening again.");
            return Task.FromResult(0);
        }

        if (IsCommand(args, "chatter"))
        {
            return SetAmbientChatter(context, args);
        }

        if (IsCommand(args, "set-failure-rate"))
        {
            return SetFailureRate(context, args);
        }

        context.WriteErrorLine(Usage);
        return Task.FromResult(1);
    }

    private static bool IsCommand(IReadOnlyList<string> args, string command)
    {
        return args.Count > 0 && string.Equals(args[0], command, StringComparison.OrdinalIgnoreCase);
    }

    private void WriteStatus(ShellContext context)
    {
        foreach (var line in _curseState.GetStatusLines("Cursed mode status"))
        {
            context.WriteLine(line);
        }
    }

    private void WriteInspect(ShellContext context)
    {
        foreach (var line in _curseState.GetInspectLines())
        {
            context.WriteLine(line);
        }
    }

    private void WriteJournal(ShellContext context)
    {
        foreach (var line in _curseState.GetJournalLines())
        {
            context.WriteLine(line);
        }
    }

    private Task<int> SetAmbientChatter(ShellContext context, IReadOnlyList<string> args)
    {
        if (args.Count != 2 || !int.TryParse(args[1], out var percent) || !_curseState.TrySetAmbientChatterChance(percent))
        {
            context.WriteErrorLine("Ambient chatter must be an integer between 0 and 25.");
            context.WriteErrorLine(Usage);
            return Task.FromResult(1);
        }

        context.WriteLine($"Ambient chatter set to {percent}%.");
        return Task.FromResult(0);
    }

    private Task<int> SetFailureRate(ShellContext context, IReadOnlyList<string> args)
    {
        if (args.Count != 2 || !int.TryParse(args[1], out var percent) || !_curseState.TrySetFailureChance(percent))
        {
            context.WriteErrorLine("Failure rate must be an integer between 0 and 50.");
            context.WriteErrorLine(Usage);
            return Task.FromResult(1);
        }

        context.WriteLine($"Failure chance set to {percent}%.");
        return Task.FromResult(0);
    }

    private Task<int> FailUsage(ShellContext context)
    {
        context.WriteErrorLine(Usage);
        return Task.FromResult(1);
    }
}
