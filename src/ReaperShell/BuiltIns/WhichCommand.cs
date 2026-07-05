using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class WhichCommand : IShellCommand
{
    private readonly CommandRegistry _commandRegistry;
    private readonly ShellSettings _settings;

    public WhichCommand(ShellSettings settings, CommandRegistry commandRegistry)
    {
        _settings = settings;
        _commandRegistry = commandRegistry;
    }

    public string Name => "which";

    public string Description => "Shows where a command comes from.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: which <command>");
            return Task.FromResult(1);
        }

        if (_settings.Aliases.TryGetValue(args[0], out var replacement))
        {
            context.WriteLine($"alias -> {replacement}");
            return Task.FromResult(0);
        }

        if (!_commandRegistry.TryGetDescriptor(args[0], out var descriptor))
        {
            if (ExternalCommandResolver.TryResolveExecutable(args[0], out var executablePath))
            {
                context.WriteLine($"external executable -> {executablePath}");
                return Task.FromResult(0);
            }

            context.WriteErrorLine($"Command '{args[0]}' is not registered and was not found on PATH.");
            return Task.FromResult(1);
        }

        if (descriptor.OriginKind == CommandOriginKind.BuiltIn)
        {
            context.WriteLine("built-in");
        }
        else
        {
            context.WriteLine($"plugin command -> {descriptor.PackName} ({descriptor.PackPath})");
        }

        return Task.FromResult(0);
    }
}
