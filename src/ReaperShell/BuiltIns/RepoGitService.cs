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

        WriteRepoStateSummary(context, repo);

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
}

internal sealed record CommitOperationResult(int ExitCode, bool CreatedCommit, bool HadNoChanges);
