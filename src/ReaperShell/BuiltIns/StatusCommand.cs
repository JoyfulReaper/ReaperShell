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
    private readonly ShellWatchService _watchService;

    public StatusCommand(
        ShellSettings settings,
        CommandRegistry commandRegistry,
        CommandPackManager commandPackManager,
        ShellWatchService watchService,
        string stateDirectory)
    {
        _settings = settings;
        _commandRegistry = commandRegistry;
        _commandPackManager = commandPackManager;
        _watchService = watchService;
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
        context.WriteLine($"WATCHED REPOS: {_watchService.WatchedRepoCount}");
        context.WriteLine($"CONFIGURED HOOKS: {_settings.Hooks.Values.Sum(rituals => rituals?.Count ?? 0)}");
        return Task.FromResult(0);
    }
}
