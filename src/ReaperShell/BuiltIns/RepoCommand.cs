using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class RepoCommand : IShellCommand
{
    private readonly CommandPackManager _commandPackManager;
    private readonly ProcessRunner _processRunner;
    private readonly ShellHost _shellHost;
    private readonly string _stateDirectory;
    private readonly ShellSettings _settings;
    private readonly ShellWatchService _watchService;
    private readonly string _workspaceRoot;

    public RepoCommand(
        ShellSettings settings,
        ProcessRunner processRunner,
        CommandPackManager commandPackManager,
        ShellHost shellHost,
        ShellWatchService watchService,
        string workspaceRoot,
        string stateDirectory)
    {
        _settings = settings;
        _processRunner = processRunner;
        _commandPackManager = commandPackManager;
        _shellHost = shellHost;
        _watchService = watchService;
        _workspaceRoot = workspaceRoot;
        _stateDirectory = stateDirectory;
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
            "add" => AddAsync(context, args, cancellationToken),
            "list" => ListAsync(context, args),
            "trust" => TrustAsync(context, args, cancellationToken),
            "untrust" => UntrustAsync(context, args, cancellationToken),
            "status" => StatusAsync(context, args, cancellationToken),
            "sync" => SyncAsync(context, args, cancellationToken),
            "build" => BuildAsync(context, args, cancellationToken),
            "load" => LoadAsync(context, args, cancellationToken),
            "unload" => UnloadAsync(context, args, cancellationToken),
            "reload" => ReloadAsync(context, args, cancellationToken),
            "new" => NewAsync(context, args, cancellationToken),
            "remove" => RemoveAsync(context, args, cancellationToken),
            "commit" => CommitAsync(context, args, cancellationToken),
            "push" => PushAsync(context, args, cancellationToken),
            "save" => SaveAsync(context, args, cancellationToken),
            "build-all" => BuildAllAsync(context, args, cancellationToken),
            "load-all" => LoadAllAsync(context, args, cancellationToken),
            "reload-all" => ReloadAllAsync(context, args, cancellationToken),
            "autosync" => AutoSyncAsync(context, args, cancellationToken),
            "watch" => WatchAsync(context, args),
            "unwatch" => UnwatchAsync(context, args),
            "watch-list" => WatchListAsync(context, args),
            _ => Task.FromResult(WriteUsage(context))
        };
    }

    private async Task<int> AddAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 3)
        {
            context.WriteErrorLine("Usage: repo add <name> <path-or-git-url>");
            return 1;
        }

        var name = args[1];
        var source = args[2];
        if (_settings.Repos.ContainsKey(name))
        {
            context.WriteErrorLine($"Repo '{name}' is already registered.");
            return 1;
        }

        CommandRepoSettings repo;
        var localCandidate = Path.GetFullPath(source, context.WorkingDirectory.FullName);
        if (Directory.Exists(localCandidate))
        {
            repo = new CommandRepoSettings
            {
                Name = name,
                Source = source,
                LocalPath = localCandidate,
                Trusted = false,
                IsGitRepo = LooksLikeGitWorkingTree(localCandidate)
            };
        }
        else if (LooksLikeGitUrl(source))
        {
            var reposRoot = GetManagedReposRoot();
            Directory.CreateDirectory(reposRoot);

            var clonePath = Path.Combine(reposRoot, name);
            if (Directory.Exists(clonePath))
            {
                context.WriteErrorLine($"The destination already exists: {clonePath}");
                return 1;
            }

            var cloneResult = await RunGitAsync(
                repoName: name,
                ["clone", source, clonePath],
                context.WorkingDirectory.FullName,
                context,
                cancellationToken);

            if (cloneResult.ExitCode != 0)
            {
                return cloneResult.ExitCode;
            }

            repo = new CommandRepoSettings
            {
                Name = name,
                Source = source,
                LocalPath = clonePath,
                Trusted = false,
                IsGitRepo = true
            };
        }
        else
        {
            context.WriteErrorLine("The source must be an existing local directory or a Git URL.");
            return 1;
        }

        _settings.Repos[name] = repo;
        await _settings.SaveAsync(_stateDirectory, cancellationToken);
        context.WriteLine($"Registered repo '{name}' at {repo.LocalPath}.");
        context.WriteLine("Newly added repos are untrusted until you run 'repo trust <name>'.");
        return 0;
    }

    private Task<int> ListAsync(ShellContext context, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: repo list");
            return Task.FromResult(1);
        }

        if (_settings.Repos.Count == 0)
        {
            context.WriteLine("No repos are registered.");
            return Task.FromResult(0);
        }

        foreach (var repo in _settings.Repos.Values.OrderBy(repo => repo.Name, StringComparer.OrdinalIgnoreCase))
        {
            var loadedState = _commandPackManager.IsLoaded(repo.Name) ? "loaded" : "unloaded";
            var trustState = repo.Trusted ? "trusted" : "untrusted";
            var gitState = repo.IsGitRepo ? "git" : "local";
            var autoSyncState = repo.AutoSyncOnSuccessfulReload ? "autosync=on" : "autosync=off";
            context.WriteLine(
                $"{repo.Name} | {gitState} | {trustState} | {loadedState} | {autoSyncState} | {repo.LocalPath}");
        }

        return Task.FromResult(0);
    }

    private async Task<int> TrustAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepo(args, "repo trust <name>", context, out var repo))
        {
            return 1;
        }

        repo.Trusted = true;
        await SaveSettingsAsync(cancellationToken);
        context.WriteLine("This repo can execute code on your machine when loaded.");
        context.WriteLine($"Marked '{repo.Name}' as trusted.");
        return 0;
    }

    private async Task<int> UntrustAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepo(args, "repo untrust <name>", context, out var repo))
        {
            return 1;
        }

        if (_commandPackManager.IsLoaded(repo.Name))
        {
            context.WriteErrorLine($"Repo '{repo.Name}' is loaded. Unload it before removing trust.");
            return 1;
        }

        repo.Trusted = false;
        _watchService.StopWatching(repo.Name, out _);
        await SaveSettingsAsync(cancellationToken);
        context.WriteLine($"Marked '{repo.Name}' as untrusted.");
        return 0;
    }

    private async Task<int> StatusAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepo(args, "repo status <name>", context, out var repo))
        {
            return 1;
        }

        if (!repo.IsGitRepo)
        {
            context.WriteLine("This repo is a local non-git command pack.");
            return 0;
        }

        var result = await RunGitAsync(
            repo.Name,
            ["status", "--short"],
            repo.LocalPath,
            context,
            cancellationToken);

        if (result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            context.WriteLine("(working tree clean)");
        }

        return result.ExitCode;
    }

    private async Task<int> SyncAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepo(args, "repo sync <name>", context, out var repo))
        {
            return 1;
        }

        return await SyncRepoAsync(context, repo, cancellationToken);
    }

    private async Task<int> BuildAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepo(args, "repo build <name>", context, out var repo))
        {
            return 1;
        }

        return await BuildRepoAsync(context, repo, cancellationToken);
    }

    private async Task<int> LoadAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepo(args, "repo load <name>", context, out var repo))
        {
            return 1;
        }

        return await LoadRepoAsync(context, repo, cancellationToken);
    }

    private async Task<int> UnloadAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepo(args, "repo unload <name>", context, out var repo))
        {
            return 1;
        }

        return await UnloadRepoIfLoadedAsync(context, repo, writeIfNotLoaded: true, cancellationToken);
    }

    private async Task<int> ReloadAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepo(args, "repo reload <name>", context, out var repo))
        {
            return 1;
        }

        return await ReloadRepoAsync(context, repo, cancellationToken);
    }

    private async Task<int> NewAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 2)
        {
            context.WriteErrorLine("Usage: repo new <name>");
            return 1;
        }

        var name = args[1];
        if (_settings.Repos.ContainsKey(name))
        {
            context.WriteErrorLine($"Repo '{name}' is already registered.");
            return 1;
        }

        var repoRoot = Path.Combine(GetManagedReposRoot(), name);
        if (Directory.Exists(repoRoot))
        {
            context.WriteErrorLine($"The destination already exists: {repoRoot}");
            return 1;
        }

        Directory.CreateDirectory(repoRoot);
        Directory.CreateDirectory(Path.Combine(repoRoot, "commands", "hello"));

        var manifest = new CommandPackManifest
        {
            Id = name,
            Name = $"{name} Pack",
            Description = $"Generated command pack '{name}'.",
            CommandsPath = "commands"
        };

        await manifest.SaveAsync(Path.Combine(repoRoot, "shellpack.json"), cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(repoRoot, "commands", "hello", "HelloCommand.csproj"),
            GetGeneratedProjectFileContents(repoRoot),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(repoRoot, "commands", "hello", "HelloCommand.cs"),
            GetGeneratedCommandContents(),
            cancellationToken);

        var repo = new CommandRepoSettings
        {
            Name = name,
            Source = repoRoot,
            LocalPath = repoRoot,
            Trusted = true,
            IsGitRepo = false
        };

        _settings.Repos[name] = repo;
        await SaveSettingsAsync(cancellationToken);

        context.WriteLine($"Created local command pack at {repoRoot}");
        context.WriteLine("repo build " + name);
        context.WriteLine("repo load " + name);
        context.WriteLine("hello");
        return 0;
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

        if (!TryGetRepoByName(args[1], context, out var repo))
        {
            return 1;
        }

        if (deleteFiles && !IsManagedRepoPath(repo.LocalPath))
        {
            context.WriteErrorLine(
                $"Refusing to delete files outside the configured state directory: {repo.LocalPath}");
            return 1;
        }

        var unloadExitCode = await UnloadRepoIfLoadedAsync(context, repo, writeIfNotLoaded: false);
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

        _settings.Repos.Remove(repo.Name);
        await SaveSettingsAsync(cancellationToken);

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

    private async Task<int> CommitAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 3)
        {
            context.WriteErrorLine("Usage: repo commit <name> \"message\"");
            return 1;
        }

        if (!TryGetRepoByName(args[1], context, out var repo))
        {
            return 1;
        }

        var commitResult = await CommitRepoAsync(context, repo, args[2], cancellationToken);
        return commitResult.ExitCode;
    }

    private async Task<int> PushAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 2)
        {
            context.WriteErrorLine("Usage: repo push <name>");
            return 1;
        }

        if (!TryGetRepoByName(args[1], context, out var repo))
        {
            return 1;
        }

        return await PushRepoAsync(context, repo, cancellationToken);
    }

    private async Task<int> SaveAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 3)
        {
            context.WriteErrorLine("Usage: repo save <name> \"message\"");
            return 1;
        }

        if (!TryGetRepoByName(args[1], context, out var repo))
        {
            return 1;
        }

        return await SaveRepoAsync(
            context,
            repo,
            args[2],
            pushWhenNothingWasCommitted: true,
            cancellationToken);
    }

    private async Task<int> BuildAllAsync(
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

    private async Task<int> LoadAllAsync(
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

    private async Task<int> ReloadAllAsync(
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

    private async Task<int> AutoSyncAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 3)
        {
            context.WriteErrorLine("Usage: repo autosync <name> <on|off>");
            return 1;
        }

        if (!TryGetRepoByName(args[1], context, out var repo))
        {
            return 1;
        }

        var autoSyncEnabled = args[2].ToLowerInvariant() switch
        {
            "on" => true,
            "off" => false,
            _ => (bool?)null
        };

        if (autoSyncEnabled is null)
        {
            context.WriteErrorLine("Usage: repo autosync <name> <on|off>");
            return 1;
        }

        repo.AutoSyncOnSuccessfulReload = autoSyncEnabled.Value;
        await SaveSettingsAsync(cancellationToken);
        context.WriteLine(
            $"Auto-sync on successful reload for '{repo.Name}' is now {(repo.AutoSyncOnSuccessfulReload ? "on" : "off")}.");
        return 0;
    }

    private Task<int> WatchAsync(ShellContext context, IReadOnlyList<string> args)
    {
        if (!_shellHost.IsInteractiveModeEnabled)
        {
            context.WriteErrorLine("Watch mode is interactive-only.");
            return Task.FromResult(1);
        }

        if (!TryGetRepo(args, "repo watch <name>", context, out var repo))
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

    private async Task<int> LoadRepoAsync(
        ShellContext context,
        CommandRepoSettings repo,
        CancellationToken cancellationToken)
    {
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

        if (repo.IsGitRepo)
        {
            var syncExitCode = await SyncRepoAsync(context, repo, cancellationToken);
            if (syncExitCode != 0)
            {
                context.WriteErrorLine($"Reload failed while syncing '{repo.Name}'.");
                await _shellHost.RunHookEventAsync(context, ShellHookEventNames.RepoReloadFailed, cancellationToken);
                return syncExitCode;
            }
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

        var saveExitCode = await SaveRepoAsync(
            context,
            repo,
            $"Update {repo.Name} command pack",
            pushWhenNothingWasCommitted: false,
            cancellationToken);

        if (saveExitCode != 0)
        {
            context.WriteErrorLine($"Reload succeeded but auto-sync failed for '{repo.Name}'.");
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

    private async Task<int> SyncRepoAsync(
        ShellContext context,
        CommandRepoSettings repo,
        CancellationToken cancellationToken)
    {
        if (!repo.IsGitRepo)
        {
            context.WriteErrorLine("Sync only works for Git-backed repos.");
            return 1;
        }

        var result = await RunGitAsync(
            repo.Name,
            ["pull", "--rebase"],
            repo.LocalPath,
            context,
            cancellationToken);

        return result.ExitCode;
    }

    private async Task<int> PushRepoAsync(
        ShellContext context,
        CommandRepoSettings repo,
        CancellationToken cancellationToken)
    {
        if (!repo.IsGitRepo)
        {
            context.WriteErrorLine($"Repo '{repo.Name}' is not a Git repo.");
            return 1;
        }

        var result = await RunGitAsync(
            repo.Name,
            ["push"],
            repo.LocalPath,
            context,
            cancellationToken);

        return result.ExitCode;
    }

    private async Task<CommitOperationResult> CommitRepoAsync(
        ShellContext context,
        CommandRepoSettings repo,
        string message,
        CancellationToken cancellationToken)
    {
        if (!repo.IsGitRepo)
        {
            context.WriteErrorLine($"Repo '{repo.Name}' is not a Git repo.");
            return new CommitOperationResult(1, false, false);
        }

        var addResult = await RunGitAsync(
            repo.Name,
            ["add", "."],
            repo.LocalPath,
            context,
            cancellationToken);

        if (addResult.ExitCode != 0)
        {
            return new CommitOperationResult(addResult.ExitCode, false, false);
        }

        var commitResult = await RunGitAsync(
            repo.Name,
            ["commit", "-m", message],
            repo.LocalPath,
            context,
            cancellationToken);

        if (commitResult.ExitCode == 0)
        {
            return new CommitOperationResult(0, true, false);
        }

        if (HasNothingToCommit(commitResult))
        {
            context.WriteLine($"Nothing to commit for '{repo.Name}'.");
            return new CommitOperationResult(0, false, true);
        }

        return new CommitOperationResult(commitResult.ExitCode, false, false);
    }

    private async Task<int> SaveRepoAsync(
        ShellContext context,
        CommandRepoSettings repo,
        string message,
        bool pushWhenNothingWasCommitted,
        CancellationToken cancellationToken)
    {
        var commitResult = await CommitRepoAsync(context, repo, message, cancellationToken);
        if (commitResult.ExitCode != 0)
        {
            return commitResult.ExitCode;
        }

        if (commitResult.HadNoChanges)
        {
            if (!pushWhenNothingWasCommitted)
            {
                context.WriteLine($"Skipped push for '{repo.Name}' because there was nothing new to commit.");
                return 0;
            }

            context.WriteLine(
                $"No new commit was created for '{repo.Name}'. Attempting push in case local commits are still pending.");
        }

        return await PushRepoAsync(context, repo, cancellationToken);
    }

    private async Task<int> RunTrustedRepoOperationAsync(
        string operationName,
        ShellContext context,
        Func<CommandRepoSettings, string?> skipReason,
        Func<CommandRepoSettings, Task<int>> operation)
    {
        var trustedRepos = _settings.Repos.Values
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

    private async Task<int> UnloadRepoIfLoadedAsync(
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

    private async Task<ProcessRunResult> RunGitAsync(
        string repoName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            "git",
            arguments,
            workingDirectory,
            context.WriteLine,
            context.WriteErrorLine,
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0 && result.StandardOutput.Length == 0 && result.StandardError.Length == 0)
        {
            context.WriteErrorLine($"Git command failed for '{repoName}'.");
        }

        return result;
    }

    private async Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
        await _settings.SaveAsync(_stateDirectory, cancellationToken);
    }

    private static int WriteUsage(ShellContext context)
    {
        context.WriteErrorLine(
            "Usage: repo <add|list|trust|untrust|status|sync|build|load|unload|reload|new|remove|commit|push|save|build-all|load-all|reload-all|autosync|watch|unwatch|watch-list> ...");
        return 1;
    }

    private string GetManagedReposRoot()
    {
        return Path.Combine(_stateDirectory, "repos");
    }

    private bool IsManagedRepoPath(string path)
    {
        var fullStateDirectory = AppendDirectorySeparator(Path.GetFullPath(_stateDirectory));
        var fullRepoPath = Path.GetFullPath(path);
        return fullRepoPath.StartsWith(fullStateDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string AppendDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool HasNothingToCommit(ProcessRunResult result)
    {
        var combinedOutput = $"{result.StandardOutput}\n{result.StandardError}";
        return combinedOutput.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) ||
               combinedOutput.Contains("no changes added to commit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeGitWorkingTree(string path)
    {
        return Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git"));
    }

    private static bool LooksLikeGitUrl(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https" or "ssh" or "git" or "file";
        }

        return source.Contains("git@", StringComparison.OrdinalIgnoreCase) ||
               source.EndsWith(".git", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetRepo(
        IReadOnlyList<string> args,
        string usage,
        ShellContext context,
        out CommandRepoSettings repo)
    {
        repo = null!;

        if (args.Count != 2)
        {
            context.WriteErrorLine($"Usage: {usage}");
            return false;
        }

        return TryGetRepoByName(args[1], context, out repo);
    }

    private bool TryGetRepoByName(string repoName, ShellContext context, out CommandRepoSettings repo)
    {
        repo = null!;

        if (!_settings.Repos.TryGetValue(repoName, out var foundRepo))
        {
            context.WriteErrorLine($"Repo '{repoName}' is not registered.");
            return false;
        }

        repo = foundRepo;
        return true;
    }

    private string GetGeneratedProjectFileContents(string repoRoot)
    {
        var projectDirectory = Path.Combine(repoRoot, "commands", "hello");
        var abstractionsProjectPath = Path.Combine(
            _workspaceRoot,
            "src",
            "ReaperShell.Abstractions",
            "ReaperShell.Abstractions.csproj");
        var relativeProjectReference = Path.GetRelativePath(projectDirectory, abstractionsProjectPath);

        return $$"""
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="{{relativeProjectReference}}" />
  </ItemGroup>

</Project>
""";
    }

    private static string GetGeneratedCommandContents()
    {
        return """
using ReaperShell.Abstractions;

namespace HelloCommand;

public sealed class HelloCommand : IShellCommand
{
    public string Name => "hello";

    public string Description => "Prints a hello message from a live-loaded command.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        context.WriteLine("Hello from a live-loaded ReaperShell command.");
        return Task.FromResult(0);
    }
}
""";
    }
}

public sealed record CommitOperationResult(int ExitCode, bool CreatedCommit, bool HadNoChanges);
