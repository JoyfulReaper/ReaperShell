using System.Text.Json;
using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class RepoCommand : IShellCommand
{
    private readonly CommandPackManager _commandPackManager;
    private readonly ProcessRunner _processRunner;
    private readonly string _stateDirectory;
    private readonly ShellSettings _settings;
    private readonly string _workspaceRoot;

    public RepoCommand(
        ShellSettings settings,
        ProcessRunner processRunner,
        CommandPackManager commandPackManager,
        string workspaceRoot,
        string stateDirectory)
    {
        _settings = settings;
        _processRunner = processRunner;
        _commandPackManager = commandPackManager;
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
            "list" => ListAsync(context),
            "trust" => TrustAsync(context, args, cancellationToken),
            "untrust" => UntrustAsync(context, args, cancellationToken),
            "status" => StatusAsync(context, args, cancellationToken),
            "sync" => SyncAsync(context, args, cancellationToken),
            "build" => BuildAsync(context, args, cancellationToken),
            "load" => LoadAsync(context, args, cancellationToken),
            "unload" => UnloadAsync(context, args),
            "reload" => ReloadAsync(context, args, cancellationToken),
            "new" => NewAsync(context, args, cancellationToken),
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

            var cloneResult = await _processRunner.RunAsync(
                "git",
                ["clone", source, clonePath],
                context.WorkingDirectory.FullName,
                context.WriteLine,
                context.WriteErrorLine,
                cancellationToken: cancellationToken);

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

    private Task<int> ListAsync(ShellContext context)
    {
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
            context.WriteLine($"{repo.Name} | {gitState} | {trustState} | {loadedState} | {repo.LocalPath}");
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
        await _settings.SaveAsync(_stateDirectory, cancellationToken);
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
        await _settings.SaveAsync(_stateDirectory, cancellationToken);
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

        var result = await _processRunner.RunAsync(
            "git",
            ["status", "--short"],
            repo.LocalPath,
            context.WriteLine,
            context.WriteErrorLine,
            cancellationToken: cancellationToken);

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

        if (!repo.IsGitRepo)
        {
            context.WriteErrorLine("Sync only works for Git-backed repos.");
            return 1;
        }

        var result = await _processRunner.RunAsync(
            "git",
            ["pull", "--rebase"],
            repo.LocalPath,
            context.WriteLine,
            context.WriteErrorLine,
            cancellationToken: cancellationToken);

        return result.ExitCode;
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

    private async Task<int> LoadAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepo(args, "repo load <name>", context, out var repo))
        {
            return 1;
        }

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

        return result.ExitCode;
    }

    private async Task<int> UnloadAsync(ShellContext context, IReadOnlyList<string> args)
    {
        if (!TryGetRepo(args, "repo unload <name>", context, out var repo))
        {
            return 1;
        }

        if (!_commandPackManager.IsLoaded(repo.Name))
        {
            context.WriteLine($"Repo '{repo.Name}' is not loaded.");
            return 0;
        }

        var result = await _commandPackManager.UnloadAsync(repo.Name, context);
        return result.ExitCode;
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

        if (!repo.Trusted)
        {
            context.WriteErrorLine($"Repo '{repo.Name}' is not trusted.");
            return 1;
        }

        if (_commandPackManager.IsLoaded(repo.Name))
        {
            var unloadResult = await _commandPackManager.UnloadAsync(repo.Name, context);
            if (unloadResult.ExitCode != 0)
            {
                context.WriteErrorLine($"Reload failed while unloading '{repo.Name}'.");
                return unloadResult.ExitCode;
            }
        }

        if (repo.IsGitRepo)
        {
            var syncResult = await _processRunner.RunAsync(
                "git",
                ["pull", "--rebase"],
                repo.LocalPath,
                context.WriteLine,
                context.WriteErrorLine,
                cancellationToken: cancellationToken);

            if (syncResult.ExitCode != 0)
            {
                context.WriteErrorLine($"Reload failed while syncing '{repo.Name}'.");
                return syncResult.ExitCode;
            }
        }

        var buildResult = await _commandPackManager.BuildAsync(
            repo,
            _settings.DefaultConfiguration,
            context,
            cancellationToken);

        if (buildResult.ExitCode != 0)
        {
            context.WriteErrorLine($"Reload failed while building '{repo.Name}'.");
            return buildResult.ExitCode;
        }

        var loadResult = await _commandPackManager.LoadAsync(
            repo,
            _settings.DefaultConfiguration,
            context,
            cancellationToken);

        if (loadResult.ExitCode != 0)
        {
            context.WriteErrorLine($"Reload failed while loading '{repo.Name}'.");
            return loadResult.ExitCode;
        }

        return 0;
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
        await _settings.SaveAsync(_stateDirectory, cancellationToken);

        context.WriteLine($"Created local command pack at {repoRoot}");
        context.WriteLine("repo build " + name);
        context.WriteLine("repo load " + name);
        context.WriteLine("hello");
        return 0;
    }

    private static int WriteUsage(ShellContext context)
    {
        context.WriteErrorLine("Usage: repo <add|list|trust|untrust|status|sync|build|load|unload|reload|new> ...");
        return 1;
    }

    private string GetManagedReposRoot()
    {
        return Path.Combine(_stateDirectory, "repos");
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

        if (!_settings.Repos.TryGetValue(args[1], out var foundRepo))
        {
            context.WriteErrorLine($"Repo '{args[1]}' is not registered.");
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
