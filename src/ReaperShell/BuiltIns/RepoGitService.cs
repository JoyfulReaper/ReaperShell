using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

internal sealed class RepoGitService
{
    private readonly ProcessRunner _processRunner;
    private readonly RepoRegistryService _registry;

    public RepoGitService(RepoRegistryService registry, ProcessRunner processRunner)
    {
        _registry = registry;
        _processRunner = processRunner;
    }

    public async Task<int> StatusAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGetRepo(args, "repo status <name>", context, out var repo))
        {
            return 1;
        }

        if (!repo.IsGitRepo)
        {
            WriteRepoStateSummary(context, repo);
            if (HasGitMetadata(repo.LocalPath))
            {
                context.WriteLine(
                    "This repo has a local .git directory, but ReaperShell still tracks it as a local non-git command pack.");
                context.WriteLine(
                    "Use `repo publish <name> <owner/repo>` to finish publishing it, or update/remove/re-add the repo if this was intentional.");
                return 0;
            }

            context.WriteLine("This repo is a local non-git command pack.");
            return 0;
        }

        var snapshot = await ReadGitRepositorySnapshotAsync(repo, context, cancellationToken);
        if (snapshot is null)
        {
            return 1;
        }

        WriteRepoStateSummary(context, repo, snapshot);
        WriteGitBranchListing(context, "Remote branches", snapshot.RemoteBranches);

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

    public async Task<int> BranchesAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGetRepo(args, "repo branches <name>", context, out var repo))
        {
            return 1;
        }

        if (!repo.IsGitRepo)
        {
            context.WriteErrorLine($"Repo '{repo.Name}' is not a Git repo.");
            return 1;
        }

        var snapshot = await ReadGitRepositorySnapshotAsync(repo, context, cancellationToken);
        if (snapshot is null)
        {
            return 1;
        }

        context.WriteLine($"Repo: {repo.Name}");
        context.WriteLine($"Path: {repo.LocalPath}");
        context.WriteLine($"Default remote branch: {snapshot.DefaultRemoteBranch ?? "(not detected)"}");
        WriteGitBranchListing(context, "Local branches", snapshot.LocalBranches, snapshot.CurrentBranch);
        WriteGitBranchListing(context, "Remote branches", snapshot.RemoteBranches);
        return 0;
    }

    public async Task<int> SwitchAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count is not 3 and not 4)
        {
            context.WriteErrorLine("Usage: repo switch <name> <branch> [--force]");
            return 1;
        }

        if (!_registry.TryGetRepoByName(args[1], context, out var repo))
        {
            return 1;
        }

        if (!repo.IsGitRepo)
        {
            context.WriteErrorLine($"Repo '{repo.Name}' is not a Git repo.");
            return 1;
        }

        var force = false;
        if (args.Count == 4)
        {
            if (!string.Equals(args[3], "--force", StringComparison.OrdinalIgnoreCase))
            {
                context.WriteErrorLine("Usage: repo switch <name> <branch> [--force]");
                return 1;
            }

            force = true;
        }

        var fetchResult = await RunGitAsync(
            repo.Name,
            ["fetch", "origin"],
            repo.LocalPath,
            context,
            cancellationToken);
        if (fetchResult.ExitCode != 0)
        {
            return fetchResult.ExitCode;
        }

        var snapshot = await ReadGitRepositorySnapshotAsync(repo, context, cancellationToken);
        if (snapshot is null)
        {
            return 1;
        }

        var switchPlan = BuildBranchSwitchPlan(args[2], snapshot.LocalBranches, snapshot.RemoteBranches);
        if (switchPlan is null)
        {
            context.WriteErrorLine($"Branch '{args[2]}' was not found in repo '{repo.Name}'.");
            context.WriteErrorLine($"Local branches: {FormatBranchList(snapshot.LocalBranches)}");
            context.WriteErrorLine($"Remote branches: {FormatBranchList(snapshot.RemoteBranches)}");
            return 1;
        }

        if (snapshot.IsDirty && !force)
        {
            context.WriteErrorLine(
                $"Repo '{repo.Name}' has uncommitted changes. Use --force to discard tracked changes before switching branches.");
            return 1;
        }

        if (snapshot.IsDirty && force)
        {
            context.WriteLine("Force switch requested. Discarding tracked working tree changes.");
        }

        var switchArguments = BuildSwitchArguments(switchPlan, force);
        var result = await RunGitAsync(
            repo.Name,
            switchArguments,
            repo.LocalPath,
            context,
            cancellationToken);

        return result.ExitCode;
    }

    public async Task<int> PullAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGetRepo(args, "repo pull <name>", context, out var repo))
        {
            return 1;
        }

        if (!repo.IsGitRepo)
        {
            context.WriteErrorLine($"Repo '{repo.Name}' is not a Git repo.");
            return 1;
        }

        var result = await RunGitAsync(
            repo.Name,
            ["pull", "--ff-only"],
            repo.LocalPath,
            context,
            cancellationToken);

        return result.ExitCode;
    }

    public async Task<int> SyncAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGetRepo(args, "repo sync <name>", context, out var repo))
        {
            return 1;
        }

        return await SyncRepoAsync(context, repo, cancellationToken);
    }

    public async Task<int> CommitAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 3)
        {
            context.WriteErrorLine("Usage: repo commit <name> \"message\"");
            return 1;
        }

        if (!_registry.TryGetRepoByName(args[1], context, out var repo))
        {
            return 1;
        }

        var commitResult = await CommitRepoAsync(context, repo, args[2], cancellationToken);
        return commitResult.ExitCode;
    }

    public async Task<int> PushAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGetRepo(args, "repo push <name>", context, out var repo))
        {
            return 1;
        }

        return await PushRepoAsync(context, repo, cancellationToken);
    }

    public async Task<int> SaveAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 3)
        {
            context.WriteErrorLine("Usage: repo save <name> \"message\"");
            return 1;
        }

        if (!_registry.TryGetRepoByName(args[1], context, out var repo))
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

    internal async Task<int> SyncRepoAsync(
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

    internal async Task<int> SaveRepoAsync(
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

    internal async Task<int> WriteBuildBannerAsync(
        ShellContext context,
        CommandRepoSettings repo,
        CancellationToken cancellationToken)
    {
        if (!repo.IsGitRepo)
        {
            return 0;
        }

        var snapshot = await ReadGitRepositorySnapshotAsync(repo, context, cancellationToken);
        if (snapshot is null)
        {
            return 1;
        }

        context.WriteLine($"Repo: {repo.Name}");
        context.WriteLine($"Branch: {snapshot.CurrentBranchDisplay}");
        context.WriteLine($"Commit: {snapshot.ShortCommitSha}");
        return 0;
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

    private async Task<ProcessRunResult> RunGitQuietAsync(
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
            onStandardOutput: null,
            onStandardError: null,
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0 && result.StandardOutput.Length == 0 && result.StandardError.Length == 0)
        {
            context.WriteErrorLine($"Git command failed for '{repoName}'.");
        }

        return result;
    }

    private static void WriteGitFailure(ShellContext context, string repoName, ProcessRunResult result)
    {
        if (result.StandardOutput.Length == 0 && result.StandardError.Length == 0)
        {
            context.WriteErrorLine($"Git command failed for '{repoName}'.");
            return;
        }

        if (result.StandardOutput.Length > 0)
        {
            context.WriteLine(result.StandardOutput.TrimEnd());
        }

        if (result.StandardError.Length > 0)
        {
            context.WriteErrorLine(result.StandardError.TrimEnd());
        }
    }

    private static bool HasNothingToCommit(ProcessRunResult result)
    {
        var combinedOutput = $"{result.StandardOutput}\n{result.StandardError}";
        return combinedOutput.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) ||
               combinedOutput.Contains("no changes added to commit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasGitMetadata(string repoPath)
    {
        return Directory.Exists(Path.Combine(repoPath, ".git")) ||
               File.Exists(Path.Combine(repoPath, ".git"));
    }

    private void WriteRepoStateSummary(ShellContext context, CommandRepoSettings repo)
    {
        context.WriteLine($"Trusted: {(repo.Trusted ? "yes" : "no")}");
        context.WriteLine($"Autoload: {(repo.AutoLoad ? "yes" : "no")}");
        context.WriteLine($"Profile load: {(_registry.HasProfileLoadLine(repo.Name) ? "yes" : "no")}");
    }

    private void WriteRepoStateSummary(
        ShellContext context,
        CommandRepoSettings repo,
        GitRepositorySnapshot snapshot)
    {
        context.WriteLine($"Repo: {repo.Name}");
        context.WriteLine($"Path: {repo.LocalPath}");
        context.WriteLine($"Branch: {snapshot.CurrentBranchDisplay}");
        context.WriteLine($"Upstream: {snapshot.UpstreamBranch ?? "(none)"}");
        context.WriteLine($"Commit: {snapshot.ShortCommitSha}");
        context.WriteLine($"Dirty: {(snapshot.IsDirty ? "yes" : "no")}");
        context.WriteLine($"Trusted: {(repo.Trusted ? "yes" : "no")}");
        context.WriteLine($"Autoload: {(repo.AutoLoad ? "yes" : "no")}");
        context.WriteLine($"Profile load: {(_registry.HasProfileLoadLine(repo.Name) ? "yes" : "no")}");
    }

    private static string[] BuildSwitchArguments(BranchSwitchPlan switchPlan, bool force)
    {
        if (switchPlan.UseTracking)
        {
            if (force)
            {
                return ["switch", "--discard-changes", "--track", switchPlan.RemoteBranchName];
            }

            return ["switch", "--track", switchPlan.RemoteBranchName];
        }

        if (force)
        {
            return ["switch", "--discard-changes", switchPlan.LocalBranchName];
        }

        return ["switch", switchPlan.LocalBranchName];
    }

    private static BranchSwitchPlan? BuildBranchSwitchPlan(
        string requestedBranch,
        IReadOnlyCollection<string> localBranches,
        IReadOnlyCollection<string> remoteBranches)
    {
        var normalizedRequestedBranch = requestedBranch.Trim();
        if (normalizedRequestedBranch.Length == 0)
        {
            return null;
        }

        var remoteBranchName = NormalizeRemoteBranchName(normalizedRequestedBranch);
        var localBranchName = NormalizeLocalBranchName(normalizedRequestedBranch);

        if (localBranches.Contains(localBranchName, StringComparer.OrdinalIgnoreCase))
        {
            return new BranchSwitchPlan(localBranchName, remoteBranchName, UseTracking: false);
        }

        if (remoteBranches.Contains(remoteBranchName, StringComparer.OrdinalIgnoreCase))
        {
            return new BranchSwitchPlan(localBranchName, remoteBranchName, UseTracking: true);
        }

        return null;
    }

    private static string NormalizeRemoteBranchName(string branchName)
    {
        if (branchName.StartsWith("origin/", StringComparison.OrdinalIgnoreCase))
        {
            return $"origin/{branchName["origin/".Length..]}";
        }

        return $"origin/{branchName}";
    }

    private static string NormalizeLocalBranchName(string branchName)
    {
        if (branchName.StartsWith("origin/", StringComparison.OrdinalIgnoreCase))
        {
            return branchName["origin/".Length..];
        }

        return branchName;
    }

    private static string FormatBranchList(IReadOnlyCollection<string> branches)
    {
        return branches.Count == 0 ? "(none)" : string.Join(", ", branches.OrderBy(branch => branch, StringComparer.OrdinalIgnoreCase));
    }

    private static void WriteGitBranchListing(
        ShellContext context,
        string heading,
        IReadOnlyCollection<string> branches,
        string? currentBranch = null)
    {
        context.WriteLine($"{heading}:");
        if (branches.Count == 0)
        {
            context.WriteLine("  (none)");
            return;
        }

        foreach (var branch in branches.OrderBy(branch => branch, StringComparer.OrdinalIgnoreCase))
        {
            var marker = !string.IsNullOrWhiteSpace(currentBranch) &&
                         string.Equals(branch, currentBranch, StringComparison.OrdinalIgnoreCase)
                ? "*"
                : " ";
            context.WriteLine($"{marker} {branch}");
        }
    }

    private async Task<GitRepositorySnapshot?> ReadGitRepositorySnapshotAsync(
        CommandRepoSettings repo,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        var currentBranch = await ReadGitValueAsync(repo, context, ["branch", "--show-current"], cancellationToken);
        if (currentBranch is null)
        {
            return null;
        }

        var shortSha = await ReadGitValueAsync(repo, context, ["rev-parse", "--short", "HEAD"], cancellationToken);
        if (shortSha is null)
        {
            return null;
        }

        var upstreamResult = await RunGitQuietAsync(
            repo.Name,
            ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"],
            repo.LocalPath,
            context,
            cancellationToken);
        string? upstreamBranch = null;
        if (upstreamResult.ExitCode == 0)
        {
            upstreamBranch = upstreamResult.StandardOutput.Trim();
            if (string.IsNullOrWhiteSpace(upstreamBranch))
            {
                upstreamBranch = null;
            }
        }

        var statusResult = await RunGitQuietAsync(
            repo.Name,
            ["status", "--porcelain"],
            repo.LocalPath,
            context,
            cancellationToken);
        if (statusResult.ExitCode != 0)
        {
            WriteGitFailure(context, repo.Name, statusResult);
            return null;
        }

        var localBranches = await ReadGitLinesAsync(
            repo,
            context,
            ["for-each-ref", "refs/heads", "--format=%(refname:short)"],
            cancellationToken);
        if (localBranches is null)
        {
            return null;
        }

        var remoteBranches = await ReadGitLinesAsync(
            repo,
            context,
            ["for-each-ref", "refs/remotes", "--format=%(refname:short)"],
            cancellationToken);
        if (remoteBranches is null)
        {
            return null;
        }

        var filteredRemoteBranches = remoteBranches
            .Where(branch => !string.Equals(branch, "origin/HEAD", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var defaultRemoteBranch = await ReadDefaultRemoteBranchAsync(repo, context, cancellationToken);

        return new GitRepositorySnapshot(
            currentBranch,
            upstreamBranch,
            shortSha,
            !string.IsNullOrWhiteSpace(statusResult.StandardOutput),
            localBranches,
            filteredRemoteBranches,
            defaultRemoteBranch);
    }

    private async Task<string?> ReadGitValueAsync(
        CommandRepoSettings repo,
        ShellContext context,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunGitQuietAsync(repo.Name, arguments, repo.LocalPath, context, cancellationToken);
        if (result.ExitCode != 0)
        {
            WriteGitFailure(context, repo.Name, result);
            return null;
        }

        return result.StandardOutput.Trim();
    }

    private async Task<string[]?> ReadGitLinesAsync(
        CommandRepoSettings repo,
        ShellContext context,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunGitQuietAsync(repo.Name, arguments, repo.LocalPath, context, cancellationToken);
        if (result.ExitCode != 0)
        {
            WriteGitFailure(context, repo.Name, result);
            return null;
        }

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<string?> ReadDefaultRemoteBranchAsync(
        CommandRepoSettings repo,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        var symbolicResult = await RunGitQuietAsync(
            repo.Name,
            ["symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"],
            repo.LocalPath,
            context,
            cancellationToken);
        if (symbolicResult.ExitCode == 0)
        {
            var branchName = symbolicResult.StandardOutput.Trim();
            return string.IsNullOrWhiteSpace(branchName) ? null : branchName;
        }

        var remoteShowResult = await RunGitQuietAsync(
            repo.Name,
            ["remote", "show", "origin"],
            repo.LocalPath,
            context,
            cancellationToken);
        if (remoteShowResult.ExitCode != 0)
        {
            return null;
        }

        var headLine = remoteShowResult.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith("HEAD branch:", StringComparison.OrdinalIgnoreCase));
        if (headLine is null)
        {
            return null;
        }

        var defaultBranch = headLine["HEAD branch:".Length..].Trim();
        return string.IsNullOrWhiteSpace(defaultBranch) ? null : $"origin/{defaultBranch}";
    }
}

internal sealed record CommitOperationResult(int ExitCode, bool CreatedCommit, bool HadNoChanges);

internal sealed record GitRepositorySnapshot(
    string CurrentBranch,
    string? UpstreamBranch,
    string ShortCommitSha,
    bool IsDirty,
    IReadOnlyList<string> LocalBranches,
    IReadOnlyList<string> RemoteBranches,
    string? DefaultRemoteBranch)
{
    public string CurrentBranchDisplay => string.IsNullOrWhiteSpace(CurrentBranch) ? "(detached HEAD)" : CurrentBranch;
}

internal sealed record BranchSwitchPlan(string LocalBranchName, string RemoteBranchName, bool UseTracking);
