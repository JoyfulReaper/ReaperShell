using ReaperShell.Abstractions;
using ReaperShell.Plugins;

namespace ReaperShell.BuiltIns;

public sealed class PluginsCommand : IShellCommand
{
    private readonly CommandPackManager _commandPackManager;

    public PluginsCommand(CommandPackManager commandPackManager)
    {
        _commandPackManager = commandPackManager;
    }

    public string Name => "plugins";

    public string Description => "Lists loaded command packs and their commands.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (_commandPackManager.LoadedPacks.Count == 0)
        {
            context.WriteLine("No command packs are currently loaded.");
            return Task.FromResult(0);
        }

        foreach (var pack in _commandPackManager.LoadedPacks.OrderBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase))
        {
            context.WriteLine($"{pack.Name} ({pack.Path})");
            context.WriteLine($"  Commands: {string.Join(", ", pack.RegisteredCommandNames)}");
        }

        return Task.FromResult(0);
    }
}
