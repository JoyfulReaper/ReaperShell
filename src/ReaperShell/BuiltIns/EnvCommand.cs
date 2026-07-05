using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class EnvCommand : IShellCommand
{
    private readonly ShellSessionState _sessionState;

    public EnvCommand(ShellSessionState sessionState)
    {
        _sessionState = sessionState;
    }

    public string Name => "env";

    public string Description => "Lists and manages session-scoped environment overrides.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            return Task.FromResult(ListOverrides(context));
        }

        return args[0].ToLowerInvariant() switch
        {
            "get" => Task.FromResult(GetVariable(context, args)),
            "set" => Task.FromResult(SetVariable(context, args)),
            "unset" => Task.FromResult(UnsetVariable(context, args)),
            _ => Task.FromResult(WriteUsage(context))
        };
    }

    private int ListOverrides(ShellContext context)
    {
        var overrides = _sessionState.GetEnvironmentVariables();
        if (overrides.Count == 0)
        {
            context.WriteLine("No session environment overrides are defined.");
            return 0;
        }

        foreach (var pair in overrides.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            context.WriteLine($"{pair.Key}={pair.Value}");
        }

        return 0;
    }

    private int GetVariable(ShellContext context, IReadOnlyList<string> args)
    {
        if (args.Count != 2)
        {
            context.WriteErrorLine("Usage: env get <name>");
            return 1;
        }

        var name = args[1];
        if (_sessionState.TryGetEnvironmentVariable(name, out var value))
        {
            context.WriteLine($"{name}={value}");
            return 0;
        }

        var inheritedValue = Environment.GetEnvironmentVariable(name);
        if (inheritedValue is not null)
        {
            context.WriteLine($"{name}={inheritedValue} (inherited)");
            return 0;
        }

        context.WriteErrorLine($"Environment variable '{name}' is not set.");
        return 1;
    }

    private int SetVariable(ShellContext context, IReadOnlyList<string> args)
    {
        if (args.Count < 3)
        {
            context.WriteErrorLine("Usage: env set <name> <value>");
            return 1;
        }

        var name = args[1];
        var value = string.Join(" ", args.Skip(2));
        if (string.IsNullOrWhiteSpace(name))
        {
            context.WriteErrorLine("Environment variable name cannot be empty.");
            return 1;
        }

        _sessionState.SetEnvironmentVariable(name, value);
        context.WriteLine($"{name}={value}");
        return 0;
    }

    private int UnsetVariable(ShellContext context, IReadOnlyList<string> args)
    {
        if (args.Count != 2)
        {
            context.WriteErrorLine("Usage: env unset <name> (removes the session override only)");
            return 1;
        }

        if (!_sessionState.RemoveEnvironmentVariable(args[1]))
        {
            context.WriteErrorLine($"Session override '{args[1]}' is not set.");
            return 1;
        }

        context.WriteLine($"Removed session override '{args[1]}'. Inherited OS environment values remain unchanged.");
        return 0;
    }

    private static int WriteUsage(ShellContext context)
    {
        context.WriteErrorLine("Usage: env [get|set|unset] ...");
        return 1;
    }
}
