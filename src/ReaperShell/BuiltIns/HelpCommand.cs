using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class HelpCommand : IShellCommand
{
    private readonly CommandRegistry _commandRegistry;

    public HelpCommand(CommandRegistry commandRegistry)
    {
        _commandRegistry = commandRegistry;
    }

    public string Name => "help";

    public string Description => "Lists all registered commands.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        foreach (var command in _commandRegistry.GetAllCommands())
        {
            context.WriteLine($"{command.Name} - {command.Description}");
        }

        return Task.FromResult(0);
    }
}
