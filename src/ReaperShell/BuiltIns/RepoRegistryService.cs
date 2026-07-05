using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

internal sealed class RepoRegistryService
{
    private readonly CommandPackManager _commandPackManager;
    private readonly ProcessRunner _processRunner;
    private readonly ShellSettings _settings;
    private readonly ShellWatchService _watchService;
    private readonly string _stateDirectory;
    private readonly string _workspaceRoot;

    public RepoRegistryService(
        ShellSettings settings,
        CommandPackManager commandPackManager,
        ProcessRunner processRunner,
        ShellWatchService watchService,
        string stateDirectory,
        string workspaceRoot)
    {
        _settings = settings;
        _commandPackManager = commandPackManager;
        _processRunner = processRunner;
        _watchService = watchService;
        _stateDirectory = stateDirectory;
        _workspaceRoot = workspaceRoot;
    }

    public async Task<int> AddAsync(
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
        if (!TryValidateRepoName(name, context))
        {
            return 1;
        }

        if (_settings.Repos.ContainsKey(name))
        {
            context.WriteErrorLine($"Repo '{name}' is already registered.");
            return 1;
        }

        CommandRepoSettings repo;
        var localCandidate = Path.GetFullPath(source, context.WorkingDirectory.FullName);
        if (Directory.Exists(localCandidate))
        {
            if (TryFindRepoByLocalPath(localCandidate, out var existingRepo))
            {
                context.WriteErrorLine($"Repo path is already registered as '{existingRepo.Name}': {localCandidate}");
                return 1;
            }

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
            if (TryFindRepoByLocalPath(clonePath, out var existingRepo))
            {
                context.WriteErrorLine($"Repo path is already registered as '{existingRepo.Name}': {clonePath}");
                return 1;
            }

            if (Directory.Exists(clonePath))
            {
                context.WriteErrorLine($"The destination already exists: {clonePath}");
                return 1;
            }

            var cloneResult = await RunGitCloneAsync(
                name,
                source,
                clonePath,
                context,
                cancellationToken);

            if (cloneResult != 0)
            {
                return cloneResult;
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
        await SaveSettingsAsync(cancellationToken);
        context.WriteLine($"Registered repo '{name}' at {repo.LocalPath}.");
        context.WriteLine("Newly added repos are untrusted until you run 'repo trust <name>'.");
        return 0;
    }

    public Task<int> ListAsync(ShellContext context, IReadOnlyList<string> args)
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

    public async Task<int> PruneDuplicatesAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: repo prune-duplicates");
            return 1;
        }

        var duplicateGroups = _settings.Repos.Values
            .GroupBy(repo => NormalizeRepoPath(repo.LocalPath), GetPathComparer())
            .Select(group => group
                .OrderBy(repo => repo.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray())
            .Where(group => group.Length > 1)
            .OrderBy(group => group[0].Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (duplicateGroups.Length == 0)
        {
            context.WriteLine("No duplicate repo registrations were found.");
            return 0;
        }

        var loadedDuplicates = duplicateGroups
            .SelectMany(group => group)
            .Where(repo => _commandPackManager.IsLoaded(repo.Name))
            .OrderBy(repo => repo.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (loadedDuplicates.Length > 0)
        {
            context.WriteErrorLine(
                $"Unload duplicate repos before pruning: {string.Join(", ", loadedDuplicates.Select(repo => repo.Name))}");
            return 1;
        }

        var removedRepoNames = new List<string>();
        foreach (var group in duplicateGroups)
        {
            var keptRepo = group[0];
            foreach (var duplicateRepo in group.Skip(1))
            {
                _watchService.StopWatching(duplicateRepo.Name, out _);
                _settings.Repos.Remove(duplicateRepo.Name);
                removedRepoNames.Add(duplicateRepo.Name);
                context.WriteLine(
                    $"Removed duplicate repo '{duplicateRepo.Name}' for path {NormalizeRepoPath(duplicateRepo.LocalPath)}. Keeping '{keptRepo.Name}'.");
            }
        }

        await SaveSettingsAsync(cancellationToken);
        context.WriteLine($"Pruned {removedRepoNames.Count} duplicate repo registration(s).");
        return 0;
    }

    public async Task<int> TrustAsync(
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
        context.WriteLine("Trusted command packs execute arbitrary code on your machine and are not sandboxed.");
        context.WriteLine("Only trust repos you control or have reviewed.");
        context.WriteLine($"Marked '{repo.Name}' as trusted.");
        return 0;
    }

    public async Task<int> UntrustAsync(
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

    public async Task<int> NewAsync(
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
        if (!TryValidateRepoName(name, context))
        {
            return 1;
        }

        if (_settings.Repos.ContainsKey(name))
        {
            context.WriteErrorLine($"Repo '{name}' is already registered.");
            return 1;
        }

        var repoRoot = Path.Combine(GetManagedReposRoot(), name);
        if (TryFindRepoByLocalPath(repoRoot, out var existingRepo))
        {
            context.WriteErrorLine($"Repo path is already registered as '{existingRepo.Name}': {repoRoot}");
            return 1;
        }

        if (Directory.Exists(repoRoot))
        {
            context.WriteErrorLine($"The destination already exists: {repoRoot}");
            return 1;
        }

        await RepoScaffolder.CreateGeneratedPackAsync(name, repoRoot, _workspaceRoot, cancellationToken);

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

    public async Task<int> AutoSyncAsync(
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

    internal void RemoveRepo(CommandRepoSettings repo)
    {
        _settings.Repos.Remove(repo.Name);
    }

    internal Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
        return _settings.SaveAsync(_stateDirectory, cancellationToken);
    }

    internal bool TryGetRepo(
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

    internal bool TryGetRepoByName(string repoName, ShellContext context, out CommandRepoSettings repo)
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

    internal bool TryFindRepoByLocalPath(string candidatePath, out CommandRepoSettings repo)
    {
        var normalizedCandidatePath = NormalizeRepoPath(candidatePath);
        var comparer = GetPathComparer();

        foreach (var existingRepo in _settings.Repos.Values.OrderBy(repo => repo.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (comparer.Equals(NormalizeRepoPath(existingRepo.LocalPath), normalizedCandidatePath))
            {
                repo = existingRepo;
                return true;
            }
        }

        repo = null!;
        return false;
    }

    internal bool IsManagedRepoPath(string path)
    {
        return CommandPackPathResolver.IsPathWithinRoot(_stateDirectory, path, allowExactMatch: false);
    }

    private async Task<int> RunGitCloneAsync(
        string repoName,
        string source,
        string clonePath,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            repoName,
            ["clone", source, clonePath],
            context.WorkingDirectory.FullName,
            context,
            cancellationToken);

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

    private string GetManagedReposRoot()
    {
        return Path.Combine(_stateDirectory, "repos");
    }

    private static StringComparer GetPathComparer()
    {
        return PathComparisonHelper.FileSystemComparer;
    }

    private static string NormalizeRepoPath(string path)
    {
        return PathComparisonHelper.NormalizeFullPath(path);
    }

    private static bool TryValidateRepoName(string name, ShellContext context)
    {
        if (!ShellNameValidator.IsLowerKebabCaseName(name))
        {
            context.WriteErrorLine(
                "Repo names must start with a lowercase letter and use lowercase kebab-case.");
            return false;
        }

        return true;
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
}
