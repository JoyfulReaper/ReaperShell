using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class DescribeCommand : IShellCommand
{
    private readonly CommandRegistry _commandRegistry;
    private readonly ShellSettings _settings;

    public DescribeCommand(ShellSettings settings, CommandRegistry commandRegistry)
    {
        _settings = settings;
        _commandRegistry = commandRegistry;
    }

    public string Name => "describe";

    public string Description => "Prints details about a command.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: describe <command>");
            return Task.FromResult(1);
        }

        var commandName = args[0];
        if (_settings.Aliases.TryGetValue(commandName, out var replacement))
        {
            context.WriteLine($"NAME: {commandName}");
            context.WriteLine("DESCRIPTION: Alias");
            context.WriteLine("SOURCE: alias");
            context.WriteLine($"EXPANSION: {replacement}");
            return Task.FromResult(0);
        }

        if (!_commandRegistry.TryGetDescriptor(commandName, out var descriptor))
        {
            if (ExternalCommandDiagnostics.TryGetInfo(commandName, _settings, out var info))
            {
                context.WriteLine($"NAME: {commandName}");
                context.WriteLine("DESCRIPTION: External executable on PATH");
                context.WriteLine("SOURCE: external");
                context.WriteLine($"PATH: {info.Path}");
                context.WriteLine($"EXTERNAL COMMAND MODE: {info.Mode}");
                context.WriteLine($"RUNNABLE: {info.RunnableText}");
                return Task.FromResult(0);
            }

            context.WriteErrorLine($"Command '{commandName}' is not registered and was not found on PATH.");
            return Task.FromResult(1);
        }

        context.WriteLine($"NAME: {descriptor.Name}");
        context.WriteLine($"DESCRIPTION: {descriptor.Description}");
        if (descriptor.OriginKind == CommandOriginKind.BuiltIn)
        {
            context.WriteLine("SOURCE: built-in");
        }
        else
        {
            context.WriteLine("SOURCE: plugin");
            context.WriteLine($"PACK: {descriptor.PackName}");
            context.WriteLine($"PATH: {descriptor.PackPath}");
        }

        return Task.FromResult(0);
    }
}
