using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

internal sealed class RepoLifecycleService
{
    private readonly CommandPackManager _commandPackManager;
    private readonly RepoGitService _gitService;
    private readonly RepoRegistryService _registry;
    private readonly ShellHost _shellHost;
    private readonly ShellSettings _settings;

    public RepoLifecycleService(
        CommandPackManager commandPackManager,
        ShellHost shellHost,
        ShellSettings settings,
        RepoGitService gitService,
        RepoRegistryService registry)
    {
        _commandPackManager = commandPackManager;
        _shellHost = shellHost;
        _settings = settings;
        _gitService = gitService;
        _registry = registry;
    }

    public async Task<int> BuildAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGetRepo(args, "repo build <name>", context, out var repo))
        {
            return 1;
        }

        return await BuildRepoAsync(context, repo, cancellationToken);
    }

    public async Task<int> LoadAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGetRepo(args, "repo load <name>", context, out var repo))
        {
            return 1;
        }

        return await LoadRepoAsync(context, repo, cancellationToken);
    }

    internal async Task<int> AutoLoadTrustedReposAsync(
        ShellContext context,
        CancellationToken cancellationToken)
    {
        var correctedInvalidAutoLoad = false;
        foreach (var repo in _settings.Repos.Values.Where(repo => repo.AutoLoad && !repo.Trusted))
        {
            context.WriteErrorLine(
                $"Skipping auto-load for untrusted repo '{repo.Name}'. AutoLoad was cleared.");
            repo.AutoLoad = false;
            correctedInvalidAutoLoad = true;
        }

        if (correctedInvalidAutoLoad)
        {
            await _registry.SaveSettingsAsync(cancellationToken);
        }

        var autoLoadRepos = _settings
            .Repos.Values
            .Where(repo => repo.Trusted && repo.AutoLoad)
            .OrderBy(repo => repo.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var repo in autoLoadRepos)
        {
            if (_commandPackManager.IsLoaded(repo.Name))
            {
                context.WriteLine($"Skipping auto-load for trusted repo '{repo.Name}' because it is already loaded.");
                continue;
            }

            context.WriteLine($"Auto-loading trusted repo '{repo.Name}'...");
            var loadExitCode = await LoadRepoAsync(context, repo, cancellationToken);
            if (loadExitCode != 0)
            {
                context.WriteErrorLine(
                    $"Failed to auto-load trusted repo '{repo.Name}'. Run `repo load {repo.Name}` for details.");
            }
        }

        return 0;
    }

    public async Task<int> UnloadAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGetRepo(args, "repo unload <name>", context, out var repo))
        {
            return 1;
        }

        return await UnloadRepoIfLoadedAsync(context, repo, writeIfNotLoaded: true, cancellationToken);
    }

    public async Task<int> ReloadAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGetRepo(args, "repo reload <name>", context, out var repo))
        {
            return 1;
        }

        return await ReloadRepoAsync(context, repo, cancellationToken);
    }

    public async Task<int> BuildAllAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: repo build-all");
            return 1;
        }

        return await RunTrustedRepoOperationAsync(
            "build-all",
            context,
            skipReason: _ => null,
            repo => BuildRepoAsync(context, repo, cancellationToken));
    }

    public async Task<int> LoadAllAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: repo load-all");
            return 1;
        }

        return await RunTrustedRepoOperationAsync(
            "load-all",
            context,
            repo => _commandPackManager.IsLoaded(repo.Name) ? "already loaded" : null,
            repo => LoadRepoAsync(context, repo, cancellationToken));
    }

    public async Task<int> ReloadAllAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: repo reload-all");
            return 1;
        }

        return await RunTrustedRepoOperationAsync(
            "reload-all",
            context,
            skipReason: _ => null,
            repo => ReloadRepoAsync(context, repo, cancellationToken));
    }

    public async Task<int> UnloadRepoIfLoadedAsync(
        ShellContext context,
        CommandRepoSettings repo,
        bool writeIfNotLoaded,
        CancellationToken cancellationToken = default,
        bool triggerUnloadedHook = true)
    {
        if (!_commandPackManager.IsLoaded(repo.Name))
        {
            if (writeIfNotLoaded)
            {
                context.WriteLine($"Repo '{repo.Name}' is not loaded.");
            }

            return 0;
        }

        context.WriteLine($"Repo '{repo.Name}' is loaded. Unloading it first.");
        var result = await _commandPackManager.UnloadAsync(repo.Name, context);
        if (result.ExitCode == 0 && triggerUnloadedHook)
        {
            await _shellHost.RunHookEventAsync(context, ShellHookEventNames.RepoUnloaded, cancellationToken);
        }

        return result.ExitCode;
    }

    private async Task<int> BuildRepoAsync(
        ShellContext context,
        CommandRepoSettings repo,
        CancellationToken cancellationToken)
    {
        if (!repo.Trusted)
        {
            context.WriteErrorLine($"Repo '{repo.Name}' is not trusted.");
            return 1;
        }

        if (repo.IsGitRepo)
        {
            var bannerExitCode = await _gitService.WriteBuildBannerAsync(context, repo, cancellationToken);
            if (bannerExitCode != 0)
            {
                return bannerExitCode;
            }
        }

        var result = await _commandPackManager.BuildAsync(
            repo,
            _settings.DefaultConfiguration,
            context,
            cancellationToken);

        return result.ExitCode;
    }

    private async Task<int> LoadRepoAsync(
        ShellContext context,
        CommandRepoSettings repo,
        CancellationToken cancellationToken,
        bool triggerLoadedHook)
    {
        if (!repo.Trusted)
        {
            context.WriteErrorLine($"Repo '{repo.Name}' is not trusted.");
            return 1;
        }

        var result = await _commandPackManager.LoadAsync(
            repo,
            _settings.DefaultConfiguration,
            context,
            cancellationToken);

        if (result.ExitCode == 0 && triggerLoadedHook)
        {
            await _shellHost.RunHookEventAsync(context, ShellHookEventNames.RepoLoaded, cancellationToken);
        }

        return result.ExitCode;
    }

    internal async Task<int> LoadRepoAsync(
        ShellContext context,
        CommandRepoSettings repo,
        CancellationToken cancellationToken)
    {
        if (_commandPackManager.IsLoaded(repo.Name))
        {
            context.WriteLine($"Repo '{repo.Name}' is already loaded.");
            return 0;
        }

        return await LoadRepoAsync(context, repo, cancellationToken, triggerLoadedHook: true);
    }

    private async Task<int> ReloadRepoAsync(
        ShellContext context,
        CommandRepoSettings repo,
        CancellationToken cancellationToken)
    {
        if (!repo.Trusted)
        {
            context.WriteErrorLine($"Repo '{repo.Name}' is not trusted.");
            await _shellHost.RunHookEventAsync(context, ShellHookEventNames.RepoReloadFailed, cancellationToken);
            return 1;
        }

        var unloadExitCode = await UnloadRepoIfLoadedAsync(
            context,
            repo,
            writeIfNotLoaded: false,
            cancellationToken,
            triggerUnloadedHook: false);
        if (unloadExitCode != 0)
        {
            context.WriteErrorLine($"Reload failed while unloading '{repo.Name}'.");
            await _shellHost.RunHookEventAsync(context, ShellHookEventNames.RepoReloadFailed, cancellationToken);
            return unloadExitCode;
        }

        var buildExitCode = await BuildRepoAsync(context, repo, cancellationToken);
        if (buildExitCode != 0)
        {
            context.WriteErrorLine($"Reload failed while building '{repo.Name}'.");
            await _shellHost.RunHookEventAsync(context, ShellHookEventNames.RepoReloadFailed, cancellationToken);
            return buildExitCode;
        }

        var loadExitCode = await LoadRepoAsync(
            context,
            repo,
            cancellationToken,
            triggerLoadedHook: false);
        if (loadExitCode != 0)
        {
            context.WriteErrorLine($"Reload failed while loading '{repo.Name}'.");
            await _shellHost.RunHookEventAsync(context, ShellHookEventNames.RepoReloadFailed, cancellationToken);
            return loadExitCode;
        }

        if (!repo.IsGitRepo || !repo.AutoSyncOnSuccessfulReload)
        {
            await _shellHost.RunHookEventAsync(context, ShellHookEventNames.RepoReloaded, cancellationToken);
            return 0;
        }

        var saveExitCode = await _gitService.SaveRepoAsync(
            context,
            repo,
            $"Update {repo.Name} command pack",
            pushWhenNothingWasCommitted: false,
            cancellationToken);

        if (saveExitCode != 0)
        {
            context.WriteWarningLine($"Reload succeeded but auto-sync failed for '{repo.Name}'.");
        }

        if (saveExitCode == 0)
        {
            await _shellHost.RunHookEventAsync(context, ShellHookEventNames.RepoReloaded, cancellationToken);
        }
        else
        {
            await _shellHost.RunHookEventAsync(context, ShellHookEventNames.RepoReloadFailed, cancellationToken);
        }

        return saveExitCode;
    }

    private async Task<int> RunTrustedRepoOperationAsync(
        string operationName,
        ShellContext context,
        Func<CommandRepoSettings, string?> skipReason,
        Func<CommandRepoSettings, Task<int>> operation)
    {
        var trustedRepos = _settings
            .Repos.Values
            .Where(repo => repo.Trusted)
            .OrderBy(repo => repo.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (trustedRepos.Length == 0)
        {
            context.WriteLine("No trusted repos are registered.");
            return 0;
        }

        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var failedRepos = new List<string>();

        foreach (var repo in trustedRepos)
        {
            var reason = skipReason(repo);
            if (reason is not null)
            {
                skipped++;
                context.WriteLine($"Skipping '{repo.Name}' because it is {reason}.");
                continue;
            }

            context.WriteLine($"{operationName}: {repo.Name}");
            var exitCode = await operation(repo);
            if (exitCode == 0)
            {
                succeeded++;
            }
            else
            {
                failed++;
                failedRepos.Add(repo.Name);
            }
        }

        context.WriteLine($"{operationName} summary: {succeeded} succeeded, {failed} failed, {skipped} skipped.");
        if (failedRepos.Count > 0)
        {
            context.WriteLine($"Failed repos: {string.Join(", ", failedRepos)}");
        }

        return failed > 0 ? 1 : 0;
    }
}
