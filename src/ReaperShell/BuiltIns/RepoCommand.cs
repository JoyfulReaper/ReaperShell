using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class RepoCommand : IShellCommand
{
    private readonly RepoGitService _gitService;
    private readonly RepoLifecycleService _lifecycleService;
    private readonly RepoPublishService _publishService;
    private readonly RepoRegistryService _registryService;
    private readonly ShellHost _shellHost;
    private readonly ShellWatchService _watchService;

    public RepoCommand(
        ShellSettings settings,
        ProcessRunner processRunner,
        CommandPackManager commandPackManager,
        ShellHost shellHost,
        ShellWatchService watchService,
        string workspaceRoot,
        string stateDirectory)
    {
        _registryService = new RepoRegistryService(
            settings,
            commandPackManager,
            processRunner,
            watchService,
            stateDirectory,
            workspaceRoot);
        _gitService = new RepoGitService(_registryService, processRunner);
        _publishService = new RepoPublishService(_registryService, processRunner);
        _lifecycleService = new RepoLifecycleService(
            commandPackManager,
            shellHost,
            settings,
            _gitService,
            _registryService);
        _registryService.SetLoadRepoAction(_lifecycleService.LoadRepoAsync);
        _shellHost = shellHost;
        _watchService = watchService;
    }

    public string Name => "repo";

    public string Description => "Manages command pack repositories.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        return args.Count == 0
            ? Task.FromResult(WriteUsage(context))
            : ExecuteSubcommandAsync(context, args, cancellationToken);
    }

    private Task<int> ExecuteSubcommandAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        return args[0].ToLowerInvariant() switch
        {
            "add" => _registryService.AddAsync(context, args, cancellationToken),
            "list" => _registryService.ListAsync(context, args),
            "prune-duplicates" => _registryService.PruneDuplicatesAsync(context, args, cancellationToken),
            "trust" => _registryService.TrustAsync(context, args, cancellationToken),
            "untrust" => _registryService.UntrustAsync(context, args, cancellationToken),
            "status" => _gitService.StatusAsync(context, args, cancellationToken),
            "branches" => _gitService.BranchesAsync(context, args, cancellationToken),
            "sync" => _gitService.SyncAsync(context, args, cancellationToken),
            "switch" => _gitService.SwitchAsync(context, args, cancellationToken),
            "pull" => _gitService.PullAsync(context, args, cancellationToken),
            "build" => _lifecycleService.BuildAsync(context, args, cancellationToken),
            "load" => _lifecycleService.LoadAsync(context, args, cancellationToken),
            "unload" => _lifecycleService.UnloadAsync(context, args, cancellationToken),
            "reload" => _lifecycleService.ReloadAsync(context, args, cancellationToken),
            "new" => _registryService.NewAsync(context, args, cancellationToken),
            "remove" => RemoveAsync(context, args, cancellationToken),
            "commit" => _gitService.CommitAsync(context, args, cancellationToken),
            "push" => _gitService.PushAsync(context, args, cancellationToken),
            "publish" => _publishService.PublishAsync(context, args, cancellationToken),
            "save" => _gitService.SaveAsync(context, args, cancellationToken),
            "build-all" => _lifecycleService.BuildAllAsync(context, args, cancellationToken),
            "load-all" => _lifecycleService.LoadAllAsync(context, args, cancellationToken),
            "reload-all" => _lifecycleService.ReloadAllAsync(context, args, cancellationToken),
            "autosync" => _registryService.AutoSyncAsync(context, args, cancellationToken),
            "watch" => WatchAsync(context, args),
            "unwatch" => UnwatchAsync(context, args),
            "watch-list" => WatchListAsync(context, args),
            _ => Task.FromResult(WriteUsage(context))
        };
    }

    internal Task<int> AutoLoadTrustedReposAsync(
        ShellContext context,
        CancellationToken cancellationToken,
        bool triggerLoadedHooks = true)
    {
        return _lifecycleService.AutoLoadTrustedReposAsync(context, cancellationToken, triggerLoadedHooks);
    }

    private async Task<int> RemoveAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count is not 2 and not 3)
        {
            context.WriteErrorLine("Usage: repo remove <name> [--delete-files]");
            return 1;
        }

        var deleteFiles = false;
        if (args.Count == 3)
        {
            if (!string.Equals(args[2], "--delete-files", StringComparison.OrdinalIgnoreCase))
            {
                context.WriteErrorLine("Usage: repo remove <name> [--delete-files]");
                return 1;
            }

            deleteFiles = true;
        }

        if (!_registryService.TryGetRepoByName(args[1], context, out var repo))
        {
            return 1;
        }

        if (deleteFiles && !_registryService.IsManagedRepoPath(repo.LocalPath))
        {
            context.WriteErrorLine(
                $"Refusing to delete files outside the configured state directory: {repo.LocalPath}");
            return 1;
        }

        var unloadExitCode = await _lifecycleService.UnloadRepoIfLoadedAsync(
            context,
            repo,
            writeIfNotLoaded: false,
            cancellationToken);
        if (unloadExitCode != 0)
        {
            context.WriteErrorLine($"Failed to unload '{repo.Name}' before removal.");
            return unloadExitCode;
        }

        _watchService.StopWatching(repo.Name, out _);

        if (deleteFiles && Directory.Exists(repo.LocalPath))
        {
            try
            {
                Directory.Delete(repo.LocalPath, recursive: true);
            }
            catch (Exception ex)
            {
                context.WriteErrorLine($"Failed to delete repo files for '{repo.Name}': {ex.Message}");
                return 1;
            }
        }

        _registryService.RemoveRepo(repo);
        await _registryService.SaveSettingsAsync(cancellationToken);

        if (deleteFiles)
        {
            context.WriteLine($"Removed repo '{repo.Name}' and deleted files at {repo.LocalPath}");
        }
        else
        {
            context.WriteLine($"Removed repo '{repo.Name}' from settings.");
            context.WriteLine($"Local files remain at {repo.LocalPath}");
        }

        return 0;
    }

    private Task<int> WatchAsync(ShellContext context, IReadOnlyList<string> args)
    {
        if (!_shellHost.IsInteractiveModeEnabled)
        {
            context.WriteErrorLine("Watch mode is interactive-only.");
            return Task.FromResult(1);
        }

        if (args.Count != 2)
        {
            context.WriteErrorLine("Usage: repo watch <name>");
            return Task.FromResult(1);
        }

        if (!_registryService.TryGetRepoByName(args[1], context, out var repo))
        {
            return Task.FromResult(1);
        }

        if (!repo.Trusted)
        {
            context.WriteErrorLine($"Repo '{repo.Name}' is not trusted.");
            return Task.FromResult(1);
        }

        if (!Directory.Exists(repo.LocalPath))
        {
            context.WriteErrorLine($"Repo path does not exist: {repo.LocalPath}");
            return Task.FromResult(1);
        }

        if (_watchService.TryStartWatching(context, repo.Name, repo.LocalPath, out var message))
        {
            context.WriteLine(message);
            return Task.FromResult(0);
        }

        context.WriteErrorLine(message);
        return Task.FromResult(1);
    }

    private Task<int> UnwatchAsync(ShellContext context, IReadOnlyList<string> args)
    {
        if (!_shellHost.IsInteractiveModeEnabled)
        {
            context.WriteErrorLine("Watch mode is interactive-only.");
            return Task.FromResult(1);
        }

        if (args.Count != 2)
        {
            context.WriteErrorLine("Usage: repo unwatch <name>");
            return Task.FromResult(1);
        }

        if (_watchService.StopWatching(args[1], out var message))
        {
            context.WriteLine(message);
            return Task.FromResult(0);
        }

        context.WriteErrorLine(message);
        return Task.FromResult(1);
    }

    private Task<int> WatchListAsync(ShellContext context, IReadOnlyList<string> args)
    {
        if (!_shellHost.IsInteractiveModeEnabled)
        {
            context.WriteErrorLine("Watch mode is interactive-only.");
            return Task.FromResult(1);
        }

        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: repo watch-list");
            return Task.FromResult(1);
        }

        var watchedRepos = _watchService.GetWatchedRepoNames();
        if (watchedRepos.Count == 0)
        {
            context.WriteLine("No repos are currently being watched.");
            return Task.FromResult(0);
        }

        foreach (var watchedRepo in watchedRepos)
        {
            context.WriteLine(watchedRepo);
        }

        return Task.FromResult(0);
    }

    private static int WriteUsage(ShellContext context)
    {
        context.WriteErrorLine(
            "Usage: repo <add|list|prune-duplicates|trust|untrust|status|branches|sync|switch|pull|build|load|unload|reload|new|remove|commit|push|publish|save|build-all|load-all|reload-all|autosync|watch|unwatch|watch-list> ...");
        return 1;
    }
}
