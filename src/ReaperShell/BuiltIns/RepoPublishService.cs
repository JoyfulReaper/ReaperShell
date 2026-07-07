using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

internal sealed class RepoPublishService
{
    private readonly ProcessRunner _processRunner;
    private readonly RepoRegistryService _registry;

    public RepoPublishService(RepoRegistryService registry, ProcessRunner processRunner)
    {
        _registry = registry;
        _processRunner = processRunner;
    }

    public async Task<int> PublishAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryParsePublishArgs(context, args, out var repoName, out var ownerRepo, out var visibility))
        {
            return 1;
        }

        if (!_registry.TryGetRepoByName(repoName, context, out var repo))
        {
            return 1;
        }

        if (repo.IsGitRepo)
        {
            context.WriteErrorLine(
                $"Repo '{repo.Name}' is already Git-backed. Use 'repo push' or 'repo save' instead.");
            return 1;
        }

        if (!Directory.Exists(repo.LocalPath))
        {
            context.WriteErrorLine($"Repo path does not exist: {repo.LocalPath}");
            return 1;
        }

        var manifestPath = Path.Combine(repo.LocalPath, "shellpack.json");
        if (!File.Exists(manifestPath))
        {
            context.WriteErrorLine($"shellpack.json was not found: {manifestPath}");
            return 1;
        }

        if (!ExternalCommandResolver.TryResolveExecutable("gh", out var ghExecutable))
        {
            context.WriteErrorLine("GitHub CLI 'gh' was not found. Install and authenticate it first.");
            return 1;
        }

        if (!ExternalCommandResolver.TryResolveExecutable("git", out var gitExecutable))
        {
            context.WriteErrorLine("Git 'git' was not found. Install it first.");
            return 1;
        }

        var gitBootstrapResult = await EnsureGitReadyAsync(
            context,
            repo,
            gitExecutable,
            cancellationToken);
        if (gitBootstrapResult != 0)
        {
            return gitBootstrapResult;
        }

        var gitRemoteExists = await HasOriginRemoteAsync(
            context,
            repo,
            gitExecutable,
            cancellationToken);
        if (gitRemoteExists is null)
        {
            return 1;
        }

        if (gitRemoteExists.HasValue && gitRemoteExists.Value)
        {
            var originUrl = await GetOriginRemoteUrlAsync(
                context,
                repo,
                gitExecutable,
                cancellationToken);
            if (originUrl is null)
            {
                return 1;
            }

            if (!IsRequestedRepoRemote(ownerRepo, originUrl))
            {
                context.WriteErrorLine(
                    $"Repo '{repo.Name}' already has an origin remote that points somewhere else: {originUrl}");
                return 1;
            }
        }

        var ghArguments = new List<string>
        {
            "repo",
            "create",
            ownerRepo,
            "--source",
            repo.LocalPath,
            "--push",
            visibility
        };

        if (!gitRemoteExists.GetValueOrDefault())
        {
            ghArguments.Add("--remote");
            ghArguments.Add("origin");
        }

        var ghResult = await _processRunner.RunAsync(
            ghExecutable,
            ghArguments,
            repo.LocalPath,
            context.WriteLine,
            context.WriteErrorLine,
            cancellationToken: cancellationToken);

        if (ghResult.ExitCode != 0)
        {
            return ghResult.ExitCode;
        }

        repo.IsGitRepo = true;
        repo.Source = ownerRepo;
        await _registry.SaveSettingsAsync(cancellationToken);

        context.WriteLine($"Published repo '{repo.Name}' to {ownerRepo}.");
        context.WriteLine($"repo status {repo.Name}");
        context.WriteLine($"repo save {repo.Name} \"message\"");
        return 0;
    }

    private async Task<int> EnsureGitReadyAsync(
        ShellContext context,
        CommandRepoSettings repo,
        string gitExecutable,
        CancellationToken cancellationToken)
    {
        var gitMetadataExists = HasGitMetadata(repo.LocalPath);
        var hasCommit = false;

        if (gitMetadataExists)
        {
            hasCommit = await HasAnyCommitAsync(context, repo, gitExecutable, cancellationToken);
            if (hasCommit && await HasUncommittedChangesAsync(context, repo, gitExecutable, cancellationToken))
            {
                context.WriteErrorLine(
                    $"Repo '{repo.Name}' already has git history and uncommitted changes. Commit or stash them before publishing.");
                return 1;
            }
        }

        if (!gitMetadataExists)
        {
            var initResult = await RunGitAsync(
                gitExecutable,
                ["init"],
                repo.LocalPath,
                context,
                cancellationToken);
            if (initResult.ExitCode != 0)
            {
                return initResult.ExitCode;
            }
        }

        if (!hasCommit)
        {
            var branchResult = await RunGitAsync(
                gitExecutable,
                ["branch", "-M", "main"],
                repo.LocalPath,
                context,
                cancellationToken);
            if (branchResult.ExitCode != 0)
            {
                return branchResult.ExitCode;
            }

            var addResult = await RunGitAsync(
                gitExecutable,
                ["add", "."],
                repo.LocalPath,
                context,
                cancellationToken);
            if (addResult.ExitCode != 0)
            {
                return addResult.ExitCode;
            }

            var commitResult = await RunGitAsync(
                gitExecutable,
                ["commit", "-m", "Initial command pack"],
                repo.LocalPath,
                context,
                cancellationToken);
            if (commitResult.ExitCode != 0)
            {
                return commitResult.ExitCode;
            }
        }

        return 0;
    }

    private async Task<bool> HasAnyCommitAsync(
        ShellContext context,
        CommandRepoSettings repo,
        string gitExecutable,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            gitExecutable,
            ["rev-parse", "--verify", "HEAD"],
            repo.LocalPath,
            context,
            cancellationToken);

        return result.ExitCode == 0;
    }

    private async Task<bool> HasUncommittedChangesAsync(
        ShellContext context,
        CommandRepoSettings repo,
        string gitExecutable,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            gitExecutable,
            ["status", "--short"],
            repo.LocalPath,
            context,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(result.StandardOutput);
    }

    private async Task<bool?> HasOriginRemoteAsync(
        ShellContext context,
        CommandRepoSettings repo,
        string gitExecutable,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            gitExecutable,
            ["remote"],
            repo.LocalPath,
            context,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return null;
        }

        var remotes = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return remotes.Any(remote => string.Equals(remote, "origin", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> GetOriginRemoteUrlAsync(
        ShellContext context,
        CommandRepoSettings repo,
        string gitExecutable,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            gitExecutable,
            ["remote", "get-url", "origin"],
            repo.LocalPath,
            context,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            context.WriteErrorLine($"Repo '{repo.Name}' has an origin remote, but its URL could not be read.");
            return null;
        }

        return result.StandardOutput.Trim();
    }

    private static bool HasGitMetadata(string repoPath)
    {
        return Directory.Exists(Path.Combine(repoPath, ".git")) ||
               File.Exists(Path.Combine(repoPath, ".git"));
    }

    private static bool IsRequestedRepoRemote(string ownerRepo, string remoteUrl)
    {
        return TryExtractOwnerRepo(remoteUrl, out var remoteOwnerRepo) &&
               string.Equals(ownerRepo, remoteOwnerRepo, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractOwnerRepo(string remoteUrl, out string ownerRepo)
    {
        ownerRepo = string.Empty;

        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return false;
        }

        var candidate = remoteUrl.Trim();
        if (candidate.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[..^4];
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                !uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length >= 2)
            {
                ownerRepo = $"{segments[^2]}/{segments[^1]}";
                return true;
            }
        }

        var colonIndex = candidate.IndexOf(':');
        if (colonIndex > 0 && candidate.Contains('@'))
        {
            var hostPart = candidate[..colonIndex];
            var atIndex = hostPart.LastIndexOf('@');
            if (atIndex >= 0)
            {
                hostPart = hostPart[(atIndex + 1)..];
            }

            if (!hostPart.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                !hostPart.Equals("www.github.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var path = candidate[(colonIndex + 1)..];
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length >= 2)
            {
                ownerRepo = $"{segments[^2]}/{segments[^1]}";
                return true;
            }
        }

        return false;
    }

    private static bool TryParsePublishArgs(
        ShellContext context,
        IReadOnlyList<string> args,
        out string repoName,
        out string ownerRepo,
        out string visibility)
    {
        repoName = string.Empty;
        ownerRepo = string.Empty;
        visibility = "--private";

        if (args.Count < 3)
        {
            context.WriteErrorLine("Usage: repo publish <name> <owner/repo> [--private|--public]");
            return false;
        }

        repoName = args[1];
        ownerRepo = args[2];

        var sawPrivate = false;
        var sawPublic = false;
        for (var index = 3; index < args.Count; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--private", StringComparison.OrdinalIgnoreCase))
            {
                sawPrivate = true;
                continue;
            }

            if (string.Equals(arg, "--public", StringComparison.OrdinalIgnoreCase))
            {
                sawPublic = true;
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal) && arg is not "-")
            {
                context.WriteErrorLine($"Unknown option: {arg}");
                return false;
            }

            context.WriteErrorLine($"Unexpected argument: {arg}");
            return false;
        }

        if (sawPrivate && sawPublic)
        {
            context.WriteErrorLine("Choose only one of --private or --public.");
            return false;
        }

        visibility = sawPublic ? "--public" : "--private";
        return true;
    }

    private async Task<ProcessRunResult> RunGitAsync(
        string gitExecutable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            gitExecutable,
            arguments,
            workingDirectory,
            context.WriteLine,
            context.WriteErrorLine,
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0 && result.StandardOutput.Length == 0 && result.StandardError.Length == 0)
        {
            context.WriteErrorLine($"Git command failed for '{workingDirectory}'.");
        }

        return result;
    }
}
