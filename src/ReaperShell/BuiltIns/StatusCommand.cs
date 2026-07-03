using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class StatusCommand : IShellCommand
{
    private readonly CommandPackManager _commandPackManager;
    private readonly CommandRegistry _commandRegistry;
    private readonly ShellSettings _settings;
    private readonly string _stateDirectory;

    public StatusCommand(
        ShellSettings settings,
        CommandRegistry commandRegistry,
        CommandPackManager commandPackManager,
        string stateDirectory)
    {
        _settings = settings;
        _commandRegistry = commandRegistry;
        _commandPackManager = commandPackManager;
        _stateDirectory = stateDirectory;
    }

    public string Name => "status";

    public string Description => "Prints shell runtime status.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        context.WriteLine("REAPER SHELL: ONLINE");
        context.WriteLine($"STATE DIR: {_stateDirectory}");
        context.WriteLine($"WORKING DIR: {context.WorkingDirectory.FullName}");
        context.WriteLine($"KNOWN REPOS: {_settings.Repos.Count}");
        context.WriteLine($"LOADED PACKS: {_commandPackManager.LoadedPacks.Count}");
        context.WriteLine($"REGISTERED COMMANDS: {_commandRegistry.GetCommandCount()}");
        context.WriteLine($"ALIASES: {_settings.Aliases.Count}");
        return Task.FromResult(0);
    }
}
