using System.Diagnostics;
using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Plugins;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class RepoBranchTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReaperShell.RepoBranchTests", Guid.NewGuid().ToString("N"));
    private readonly string _stateDirectory;
    private readonly string _workspaceRoot = WorkspaceRootResolver.FindWorkspaceRoot();
    private string _gitExecutablePath = string.Empty;

    public RepoBranchTests()
    {
        Directory.CreateDirectory(_root);
        _stateDirectory = Path.Combine(_root, "state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public Task InitializeAsync()
    {
        if (!ExternalCommandResolver.TryResolveExecutable("git", out _gitExecutablePath))
        {
            throw new InvalidOperationException("Git was not found on PATH, so RepoBranchTests cannot run.");
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task StatusAndBranchesShowCurrentAndRemoteBranches()
    {
        var harness = await CreateBranchHarnessAsync("iis-tools");

        var status = await RunRepoAsync(harness.RepoCommand, "status", "iis-tools");
        var branches = await RunRepoAsync(harness.RepoCommand, "branches", "iis-tools");

        Assert.Equal(0, status.ExitCode);
        Assert.Equal(0, branches.ExitCode);
        Assert.Contains("Repo: iis-tools", status.StdOut);
        Assert.Contains("Path:", status.StdOut);
        Assert.Contains("Branch: main", status.StdOut);
        Assert.Contains("Upstream: origin/main", status.StdOut);
        Assert.Contains("Commit:", status.StdOut);
        Assert.Contains("Dirty: no", status.StdOut);
        Assert.Contains("origin/dev", status.StdOut);

        Assert.Contains("Repo: iis-tools", branches.StdOut);
        Assert.Contains("Default remote branch: origin/main", branches.StdOut);
        Assert.Contains("* main", branches.StdOut);
        Assert.Contains("origin/dev", branches.StdOut);
    }

    [Fact]
    public async Task SwitchCreatesTrackingBranchAndReloadShowsSwitchedBranch()
    {
        var harness = await CreateBranchHarnessAsync("iis-tools");

        var switchResult = await RunRepoAsync(harness.RepoCommand, "switch", "iis-tools", "dev");
        var statusAfterSwitch = await RunRepoAsync(harness.RepoCommand, "status", "iis-tools");
        var reloadResult = await RunRepoAsync(harness.RepoCommand, "reload", "iis-tools");

        Assert.Equal(0, switchResult.ExitCode);
        Assert.Equal(0, statusAfterSwitch.ExitCode);
        Assert.Equal(0, reloadResult.ExitCode);
        Assert.Contains("Branch: dev", statusAfterSwitch.StdOut);
        Assert.Contains("Upstream: origin/dev", statusAfterSwitch.StdOut);
        Assert.Contains("Repo: iis-tools", reloadResult.StdOut);
        Assert.Contains("Branch: dev", reloadResult.StdOut);
        Assert.Contains("Commit:", reloadResult.StdOut);
    }

    [Fact]
    public async Task ReloadDoesNotPullOrRebaseAutomatically()
    {
        var harness = await CreateBranchHarnessAsync("iis-tools");

        var switchResult = await RunRepoAsync(harness.RepoCommand, "switch", "iis-tools", "dev");
        Assert.Equal(0, switchResult.ExitCode);

        var beforeReloadCommit = (await RunGitAsync(harness.RepoRoot, "rev-parse", "HEAD")).StdOut.Trim();
        await AdvanceRemoteBranchAsync(
            harness.RemoteRoot,
            "dev",
            "Hello from a dev branch.",
            "Hello from a remote dev branch.",
            "Advance dev branch");

        var reloadResult = await RunRepoAsync(harness.RepoCommand, "reload", "iis-tools");
        var afterReloadCommit = (await RunGitAsync(harness.RepoRoot, "rev-parse", "HEAD")).StdOut.Trim();

        Assert.Equal(0, reloadResult.ExitCode);
        Assert.Equal(beforeReloadCommit, afterReloadCommit);
        Assert.Contains("Repo: iis-tools", reloadResult.StdOut);
        Assert.Contains("Branch: dev", reloadResult.StdOut);
        Assert.Contains("Commit:", reloadResult.StdOut);
    }

    [Fact]
    public async Task SwitchRefusesDirtyTreeUnlessForced()
    {
        var harness = await CreateBranchHarnessAsync("iis-tools");
        var commandSourcePath = Path.Combine(harness.RepoRoot, "commands", "hello", "HelloCommand.cs");
        await File.WriteAllTextAsync(commandSourcePath, """
using ReaperShell.Abstractions;

namespace HelloCommand;

public sealed class HelloCommand : IShellCommand
{
    public string Name => "hello";

    public string Description => "Prints a dirty tree marker.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        context.WriteLine("dirty-tree change");
        return Task.FromResult(0);
    }
}
""");

        var refused = await RunRepoAsync(harness.RepoCommand, "switch", "iis-tools", "dev");
        var status = await RunRepoAsync(harness.RepoCommand, "status", "iis-tools");
        var branchBeforeForce = (await RunGitAsync(harness.RepoRoot, "rev-parse", "--abbrev-ref", "HEAD")).StdOut.Trim();

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("tracked working tree changes", refused.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, status.ExitCode);
        Assert.Contains("Branch:", status.StdOut);
        Assert.Contains("Dirty: yes", status.StdOut);
        Assert.Equal("main", branchBeforeForce);

        var forced = await RunRepoAsync(harness.RepoCommand, "switch", "iis-tools", "dev", "--force");
        Assert.Equal(0, forced.ExitCode);
        Assert.Contains("Force switch requested. Discarding tracked working tree changes. Untracked files may remain.", forced.StdOut);
        Assert.Contains("Branch: dev", (await RunRepoAsync(harness.RepoCommand, "status", "iis-tools")).StdOut);
        Assert.Contains("Hello from a dev branch.", await File.ReadAllTextAsync(commandSourcePath));
    }

    [Fact]
    public async Task SwitchAllowsUntrackedOnlyWithoutForce()
    {
        var harness = await CreateBranchHarnessAsync("iis-tools");
        var untrackedFilePath = Path.Combine(harness.RepoRoot, "notes.txt");
        await File.WriteAllTextAsync(untrackedFilePath, "temporary notes");

        var result = await RunRepoAsync(harness.RepoCommand, "switch", "iis-tools", "dev");
        var status = await RunRepoAsync(harness.RepoCommand, "status", "iis-tools");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("untracked files only", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Branch: dev", status.StdOut);
        Assert.True(File.Exists(untrackedFilePath));
    }

    [Fact]
    public async Task SwitchRejectsUnknownBranchWithCandidates()
    {
        var harness = await CreateBranchHarnessAsync("iis-tools");

        var result = await RunRepoAsync(harness.RepoCommand, "switch", "iis-tools", "nope");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Branch 'nope' was not found in repo 'iis-tools'.", result.StdErr);
        Assert.Contains("Local branches:", result.StdErr);
        Assert.Contains("Remote branches:", result.StdErr);
        Assert.Contains("origin/dev", result.StdErr);
    }

    [Fact]
    public async Task PullFastForwardsTheCurrentBranch()
    {
        var harness = await CreateBranchHarnessAsync("iis-tools");
        var beforeCommit = (await RunGitAsync(harness.RepoRoot, "rev-parse", "--short", "HEAD")).StdOut.Trim();

        await AdvanceRemoteMainAsync(harness.RemoteRoot);

        var pullResult = await RunRepoAsync(harness.RepoCommand, "pull", "iis-tools");
        var afterCommit = (await RunGitAsync(harness.RepoRoot, "rev-parse", "--short", "HEAD")).StdOut.Trim();

        Assert.Equal(0, pullResult.ExitCode);
        Assert.NotEqual(beforeCommit, afterCommit);
    }

    [Fact]
    public async Task SyncUsesFastForwardOnly()
    {
        var harness = await CreateBranchHarnessAsync("iis-tools");

        var commandSourcePath = Path.Combine(harness.RepoRoot, "commands", "hello", "HelloCommand.cs");
        var originalCommandSource = await File.ReadAllTextAsync(commandSourcePath);
        await File.WriteAllTextAsync(
            commandSourcePath,
            originalCommandSource.Replace(
                "Hello from a live-loaded ReaperShell command.",
                "Hello from a local branch commit."));

        await RunGitAsync(harness.RepoRoot, "add", ".");
        await RunGitAsync(harness.RepoRoot, "commit", "-m", "Local main branch changes");
        var beforeSyncCommit = (await RunGitAsync(harness.RepoRoot, "rev-parse", "--short", "HEAD")).StdOut.Trim();

        await AdvanceRemoteMainAsync(harness.RemoteRoot);

        var syncResult = await RunRepoAsync(harness.RepoCommand, "sync", "iis-tools");
        var afterSyncCommit = (await RunGitAsync(harness.RepoRoot, "rev-parse", "--short", "HEAD")).StdOut.Trim();

        Assert.NotEqual(0, syncResult.ExitCode);
        Assert.Equal(beforeSyncCommit, afterSyncCommit);
    }

    private async Task<BranchHarness> CreateBranchHarnessAsync(string repoName)
    {
        var repoRoot = Path.Combine(_root, repoName);
        var remoteRoot = Path.Combine(_root, $"{repoName}.remote.git");
        await RepoScaffolder.CreateGeneratedPackAsync(repoName, repoRoot, _workspaceRoot, CancellationToken.None);

        var commandSourcePath = Path.Combine(repoRoot, "commands", "hello", "HelloCommand.cs");
        var originalCommandSource = await File.ReadAllTextAsync(commandSourcePath);

        await RunGitAsync(remoteRoot, "init", "--bare");
        await RunGitAsync(remoteRoot, "symbolic-ref", "HEAD", "refs/heads/main");

        await RunGitAsync(repoRoot, "init", "-b", "main");
        await RunGitAsync(repoRoot, "remote", "add", "origin", remoteRoot);
        await RunGitAsync(repoRoot, "add", ".");
        await RunGitAsync(repoRoot, "commit", "-m", "Seed main branch");
        await RunGitAsync(repoRoot, "push", "-u", "origin", "main");

        await RunGitAsync(repoRoot, "switch", "-c", "dev");
        await File.WriteAllTextAsync(commandSourcePath, originalCommandSource.Replace(
            "Hello from a live-loaded ReaperShell command.",
            "Hello from a dev branch."));
        await RunGitAsync(repoRoot, "add", ".");
        await RunGitAsync(repoRoot, "commit", "-m", "Dev branch changes");
        await RunGitAsync(repoRoot, "push", "-u", "origin", "dev");
        await RunGitAsync(repoRoot, "switch", "main");
        await RunGitAsync(repoRoot, "fetch", "origin");

        var settings = new ShellSettings();
        settings.Repos[repoName] = new CommandRepoSettings
        {
            Name = repoName,
            Source = repoRoot,
            LocalPath = repoRoot,
            Trusted = true,
            IsGitRepo = true
        };

        return new BranchHarness(settings, CreateRepoCommand(settings), repoRoot, remoteRoot, commandSourcePath);
    }

    private RepoCommand CreateRepoCommand(ShellSettings settings)
    {
        var sessionState = new ShellSessionState();
        var processRunner = new ProcessRunner(sessionState);
        var registry = new CommandRegistry();
        var parser = new CommandParser();
        var host = new ShellHost(parser, registry, new ShellLifetime(), processRunner, settings, _stateDirectory, sessionState);
        var watchService = new ShellWatchService(host);
        var commandPackManager = new CommandPackManager(registry, processRunner);
        return new RepoCommand(settings, processRunner, commandPackManager, host, watchService, _workspaceRoot, _stateDirectory);
    }

    private async Task<CommandResult> RunRepoAsync(
        RepoCommand repoCommand,
        params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(stdout, stderr, new DirectoryInfo(_root), services: null, CancellationToken.None);

        var exitCode = await repoCommand.ExecuteAsync(context, args, CancellationToken.None);
        return new CommandResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private async Task AdvanceRemoteMainAsync(string remoteRoot)
    {
        await AdvanceRemoteBranchAsync(
            remoteRoot,
            "main",
            "Hello from a live-loaded ReaperShell command.",
            "Hello from a fast-forwarded branch.",
            "Advance main branch");
    }

    private async Task AdvanceRemoteBranchAsync(
        string remoteRoot,
        string branchName,
        string sourceText,
        string replacementText,
        string commitMessage)
    {
        var cloneParent = Path.Combine(_root, "remote-clone-parent");
        var cloneRoot = Path.Combine(cloneParent, "clone");
        Directory.CreateDirectory(cloneParent);
        await RunGitAsync(cloneParent, "clone", remoteRoot, cloneRoot);

        if (!string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase))
        {
            await RunGitAsync(cloneRoot, "switch", "--track", $"origin/{branchName}");
        }

        var commandSourcePath = Path.Combine(cloneRoot, "commands", "hello", "HelloCommand.cs");
        var source = await File.ReadAllTextAsync(commandSourcePath);
        await File.WriteAllTextAsync(
            commandSourcePath,
            source.Replace(sourceText, replacementText));

        await RunGitAsync(cloneRoot, "add", ".");
        await RunGitAsync(cloneRoot, "commit", "-m", commitMessage);
        await RunGitAsync(cloneRoot, "push");
    }

    private async Task<CommandResult> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Directory.CreateDirectory(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = _gitExecutablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_AUTHOR_NAME"] = "ReaperShell Test";
        startInfo.Environment["GIT_AUTHOR_EMAIL"] = "reapershell-test@example.com";
        startInfo.Environment["GIT_COMMITTER_NAME"] = "ReaperShell Test";
        startInfo.Environment["GIT_COMMITTER_EMAIL"] = "reapershell-test@example.com";

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                stdout.WriteLine(eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                stderr.WriteLine(eventArgs.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start git process in '{workingDirectory}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed record BranchHarness(
        ShellSettings Settings,
        RepoCommand RepoCommand,
        string RepoRoot,
        string RemoteRoot,
        string CommandSourcePath);

    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}
