using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class AliasCommand : IShellCommand
{
    private readonly CommandRegistry _commandRegistry;
    private readonly CommandParser _commandParser = new();
    private readonly ShellSettings _settings;
    private readonly string _stateDirectory;

    public AliasCommand(ShellSettings settings, CommandRegistry commandRegistry, string stateDirectory)
    {
        _settings = settings;
        _commandRegistry = commandRegistry;
        _stateDirectory = stateDirectory;
    }

    public string Name => "alias";

    public string Description => "Lists and manages command aliases.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            return ListAliases(context);
        }

        return args[0].ToLowerInvariant() switch
        {
            "set" => await SetAliasAsync(context, args, cancellationToken),
            "remove" => await RemoveAliasAsync(context, args, cancellationToken),
            "clear" => await ClearAliasesAsync(context, args, cancellationToken),
            "show" => ShowAlias(context, args),
            _ => WriteUsage(context)
        };
    }

    private int ListAliases(ShellContext context)
    {
        if (_settings.Aliases.Count == 0)
        {
            context.WriteLine("No aliases are defined.");
            return 0;
        }

        foreach (var alias in _settings.Aliases.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            context.WriteLine($"{alias.Key} -> {alias.Value}");
        }

        return 0;
    }

    private async Task<int> SetAliasAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count < 3)
        {
            context.WriteErrorLine("Usage: alias set <name> <replacement>");
            return 1;
        }

        var aliasName = args[1];
        if (string.IsNullOrWhiteSpace(aliasName) || aliasName.Any(char.IsWhiteSpace))
        {
            context.WriteErrorLine("Alias names cannot contain whitespace.");
            return 1;
        }

        if (_commandRegistry.IsBuiltIn(aliasName))
        {
            context.WriteErrorLine($"Cannot replace built-in command '{aliasName}' with an alias.");
            return 1;
        }

        var replacement = string.Join(" ", args.Skip(2));
        var replacementTokens = _commandParser.Parse(replacement);
        if (replacementTokens.Count == 0)
        {
            context.WriteErrorLine("Alias replacement cannot be empty.");
            return 1;
        }

        if (string.Equals(aliasName, replacementTokens[0], StringComparison.OrdinalIgnoreCase))
        {
            context.WriteErrorLine("Alias recursion is not allowed.");
            return 1;
        }

        _settings.Aliases[aliasName] = replacement;
        await _settings.SaveAsync(_stateDirectory, cancellationToken);
        context.WriteLine($"Alias '{aliasName}' set to '{replacement}'.");
        return 0;
    }

    private async Task<int> RemoveAliasAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 2)
        {
            context.WriteErrorLine("Usage: alias remove <name>");
            return 1;
        }

        if (!_settings.Aliases.Remove(args[1]))
        {
            context.WriteErrorLine($"Alias '{args[1]}' is not defined.");
            return 1;
        }

        await _settings.SaveAsync(_stateDirectory, cancellationToken);
        context.WriteLine($"Alias '{args[1]}' removed.");
        return 0;
    }

    private async Task<int> ClearAliasesAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: alias clear");
            return 1;
        }

        _settings.Aliases.Clear();
        await _settings.SaveAsync(_stateDirectory, cancellationToken);
        context.WriteLine("All aliases cleared.");
        return 0;
    }

    private int ShowAlias(ShellContext context, IReadOnlyList<string> args)
    {
        if (args.Count != 2)
        {
            context.WriteErrorLine("Usage: alias show <name>");
            return 1;
        }

        if (!_settings.Aliases.TryGetValue(args[1], out var replacement))
        {
            context.WriteErrorLine($"Alias '{args[1]}' is not defined.");
            return 1;
        }

        context.WriteLine($"{args[1]} -> {replacement}");
        return 0;
    }

    private static int WriteUsage(ShellContext context)
    {
        context.WriteErrorLine("Usage: alias <set|remove|clear|show>");
        return 1;
    }
}
