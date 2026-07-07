using System.Diagnostics;
using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Plugins;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class RepoTrustTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReaperShell.RepoTrustTests", Guid.NewGuid().ToString("N"));
    private readonly string _workspaceRoot = WorkspaceRootResolver.FindWorkspaceRoot();
    private readonly string _stateDirectory;

    public RepoTrustTests()
    {
        Directory.CreateDirectory(_root);
        _stateDirectory = Path.Combine(_root, "state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public Task InitializeAsync()
    {
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
    public async Task TrustWithoutOptionsKeepsExistingBehaviorAndSuggestsNextSteps()
    {
        var harness = await CreateHarnessAsync("trust-basic");
        var result = await RunRepoAsync(harness.RepoCommand, "trust", "trust-basic");

        Assert.Equal(0, result.ExitCode);
        Assert.True(harness.Settings.Repos["trust-basic"].Trusted);
        Assert.False(harness.Settings.Repos["trust-basic"].AutoLoad);
        Assert.Contains("Marked 'trust-basic' as trusted.", result.StdOut);
        Assert.Contains("To load now: repo load trust-basic", result.StdOut);
        Assert.Contains("To auto-load on startup: repo trust trust-basic --autoload", result.StdOut);
        Assert.Contains("To add to profile: repo trust trust-basic --profile", result.StdOut);
    }

    [Fact]
    public async Task TrustWithAutoloadSetsTrustedAndAutoload()
    {
        var harness = await CreateHarnessAsync("trust-autoload");
        var result = await RunRepoAsync(harness.RepoCommand, "trust", "trust-autoload", "--autoload");

        Assert.Equal(0, result.ExitCode);
        Assert.True(harness.Settings.Repos["trust-autoload"].Trusted);
        Assert.True(harness.Settings.Repos["trust-autoload"].AutoLoad);
        Assert.Contains("will automatically load on startup", result.StdOut);
    }

    [Fact]
    public async Task TrustWithLoadNowCallsExistingLoadPath()
    {
        var harness = await CreateBuiltHarnessAsync("trust-load-now");
        var result = await RunRepoAsync(harness.RepoCommand, "trust", "trust-load-now", "--load-now");

        Assert.Equal(0, result.ExitCode);
        Assert.True(harness.Settings.Repos["trust-load-now"].Trusted);
        Assert.False(harness.Settings.Repos["trust-load-now"].AutoLoad);
        Assert.Contains("Loaded commands:", result.StdOut);
    }

    [Fact]
    public async Task TrustWithAutoloadAndLoadNowDoesBoth()
    {
        var harness = await CreateBuiltHarnessAsync("trust-both");
        var result = await RunRepoAsync(harness.RepoCommand, "trust", "trust-both", "--autoload", "--load-now");

        Assert.Equal(0, result.ExitCode);
        Assert.True(harness.Settings.Repos["trust-both"].Trusted);
        Assert.True(harness.Settings.Repos["trust-both"].AutoLoad);
        Assert.Contains("will automatically load on startup", result.StdOut);
        Assert.Contains("Loaded commands:", result.StdOut);
    }

    [Fact]
    public async Task TrustWithProfileAppendsRepoLoadLine()
    {
        var harness = await CreateHarnessAsync("trust-profile");
        var profilePath = RepoProfileService.GetProfilePath(_stateDirectory);

        var result = await RunRepoAsync(harness.RepoCommand, "trust", "trust-profile", "--profile");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(profilePath));
        Assert.Contains("Added 'repo load trust-profile' to profile", result.StdOut);
        Assert.Equal(1, CountProfileLoadLines(profilePath, "trust-profile"));
    }

    [Fact]
    public async Task TrustWithAutoloadAndProfileSetsBoth()
    {
        var harness = await CreateHarnessAsync("trust-autoload-profile");
        var profilePath = RepoProfileService.GetProfilePath(_stateDirectory);

        var result = await RunRepoAsync(harness.RepoCommand, "trust", "trust-autoload-profile", "--autoload", "--profile");

        Assert.Equal(0, result.ExitCode);
        Assert.True(harness.Settings.Repos["trust-autoload-profile"].AutoLoad);
        Assert.True(harness.Settings.Repos["trust-autoload-profile"].Trusted);
        Assert.Equal(1, CountProfileLoadLines(profilePath, "trust-autoload-profile"));
        Assert.Contains("will automatically load on startup", result.StdOut);
        Assert.Contains("Added 'repo load trust-autoload-profile' to profile", result.StdOut);
    }

    [Fact]
    public async Task TrustWithProfileDoesNotDuplicateExistingLine()
    {
        var harness = await CreateHarnessAsync("trust-profile-dup");
        var profilePath = RepoProfileService.GetProfilePath(_stateDirectory);
        await File.WriteAllTextAsync(profilePath, "repo load trust-profile-dup" + Environment.NewLine);

        var firstResult = await RunRepoAsync(harness.RepoCommand, "trust", "trust-profile-dup", "--profile");
        var secondResult = await RunRepoAsync(harness.RepoCommand, "trust", "trust-profile-dup", "--profile");

        Assert.Equal(0, firstResult.ExitCode);
        Assert.Equal(0, secondResult.ExitCode);
        Assert.Equal(1, CountProfileLoadLines(profilePath, "trust-profile-dup"));
        Assert.Contains("Profile already contains 'repo load trust-profile-dup'", secondResult.StdOut);
    }

    [Fact]
    public async Task UntrustClearsAutoloadAndRemovesProfileLine()
    {
        var harness = await CreateHarnessAsync("trust-untrust", trusted: true, autoLoad: true);
        var profilePath = RepoProfileService.GetProfilePath(_stateDirectory);
        await RepoProfileService.AppendRepoLoadLineAsync(profilePath, "trust-untrust", CancellationToken.None);

        var result = await RunRepoAsync(harness.RepoCommand, "untrust", "trust-untrust");

        Assert.Equal(0, result.ExitCode);
        Assert.False(harness.Settings.Repos["trust-untrust"].Trusted);
        Assert.False(harness.Settings.Repos["trust-untrust"].AutoLoad);
        Assert.Equal(0, CountProfileLoadLines(profilePath, "trust-untrust"));
        Assert.Contains("Removed 'repo load trust-untrust' from profile", result.StdOut);
    }

    [Fact]
    public async Task StartupAutoloadLoadsTrustedAutoloadRepos()
    {
        var harness = await CreateBuiltHarnessAsync("autoload-repo", trusted: true, autoLoad: true);
        var result = await RunAutoLoadAsync(harness.RepoCommand);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Auto-loading trusted repo 'autoload-repo'...", result.StdOut);
        Assert.Contains("Loaded commands:", result.StdOut);
    }

    [Fact]
    public async Task StartupAutoloadDoesNotLoadDisabledOrUntrustedRepos()
    {
        var harness = await CreateBuiltHarnessAsync("disabled-repo", trusted: true, autoLoad: false);
        var untrustedRepoRoot = await CreatePackAsync("untrusted-repo");
        harness.Settings.Repos["untrusted-repo"] = new CommandRepoSettings
        {
            Name = "untrusted-repo",
            Source = untrustedRepoRoot,
            LocalPath = untrustedRepoRoot,
            Trusted = false,
            AutoLoad = true
        };

        var result = await RunAutoLoadAsync(harness.RepoCommand);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Skipping auto-load for untrusted repo 'untrusted-repo'. AutoLoad was cleared.", result.StdErr);
        Assert.False(harness.Settings.Repos["untrusted-repo"].AutoLoad);
        Assert.DoesNotContain("Auto-loading trusted repo 'disabled-repo'", result.StdOut);
    }

    [Fact]
    public async Task StartupAutoloadSkipsAlreadyLoadedRepos()
    {
        var harness = await CreateBuiltHarnessAsync("already-loaded", trusted: true, autoLoad: true);
        var first = await RunAutoLoadAsync(harness.RepoCommand);
        var second = await RunAutoLoadAsync(harness.RepoCommand);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Contains("Skipping auto-load for trusted repo 'already-loaded' because it is already loaded.", second.StdOut);
    }

    [Fact]
    public async Task StartupAutoloadFailureDoesNotCrashShellStartup()
    {
        var harness = await CreateHarnessAsync("broken-autoload", trusted: true, autoLoad: true);
        var result = await RunAutoLoadAsync(harness.RepoCommand);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Failed to auto-load trusted repo 'broken-autoload'. Run `repo load broken-autoload` for details.", result.StdErr);
    }

    [Fact]
    public async Task RepoListShowsTrustedAndAutoloadState()
    {
        var harness = await CreateHarnessAsync("list-state", trusted: true, autoLoad: true);
        var result = await RunRepoAsync(harness.RepoCommand, "list");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("list-state | local | trusted | autoload=on", result.StdOut);
    }

    [Fact]
    public async Task RepoStatusShowsTrustedAutoloadAndProfileLoadState()
    {
        var harness = await CreateHarnessAsync("status-state", trusted: true, autoLoad: false);
        var profilePath = RepoProfileService.GetProfilePath(_stateDirectory);
        await RepoProfileService.AppendRepoLoadLineAsync(profilePath, "status-state", CancellationToken.None);

        var result = await RunRepoAsync(harness.RepoCommand, "status", "status-state");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Trusted: yes", result.StdOut);
        Assert.Contains("Autoload: no", result.StdOut);
        Assert.Contains("Profile load: yes", result.StdOut);
    }

    private async Task<(ShellSettings Settings, RepoCommand RepoCommand)> CreateHarnessAsync(
        string repoName,
        bool trusted = false,
        bool autoLoad = false)
    {
        var repoRoot = await CreatePackAsync(repoName);
        var settings = new ShellSettings();
        settings.Repos[repoName] = new CommandRepoSettings
        {
            Name = repoName,
            Source = repoRoot,
            LocalPath = repoRoot,
            Trusted = trusted,
            AutoLoad = autoLoad
        };

        return (settings, CreateRepoCommand(settings));
    }

    private async Task<(ShellSettings Settings, RepoCommand RepoCommand)> CreateBuiltHarnessAsync(
        string repoName,
        bool trusted = false,
        bool autoLoad = false)
    {
        var harness = await CreateHarnessAsync(repoName, trusted, autoLoad);
        await BuildPackAsync(harness.Settings.Repos[repoName]);
        return harness;
    }

    private async Task<string> CreatePackAsync(string repoName)
    {
        var repoRoot = Path.Combine(_root, repoName);
        await RepoScaffolder.CreateGeneratedPackAsync(repoName, repoRoot, _workspaceRoot, CancellationToken.None);
        return repoRoot;
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
        return new RepoCommand(settings, processRunner, commandPackManager, host, watchService, _root, _stateDirectory);
    }

    private async Task BuildPackAsync(CommandRepoSettings repo)
    {
        var sessionState = new ShellSessionState();
        var processRunner = new ProcessRunner(sessionState);
        var registry = new CommandRegistry();
        var commandPackManager = new CommandPackManager(registry, processRunner);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(stdout, stderr, new DirectoryInfo(_root), services: null, CancellationToken.None);

        var result = await commandPackManager.BuildAsync(repo, "Debug", context, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
    }

    private static async Task<CommandResult> RunRepoAsync(
        RepoCommand repoCommand,
        params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(stdout, stderr, new DirectoryInfo(Path.GetTempPath()), services: null, CancellationToken.None);

        var exitCode = await repoCommand.ExecuteAsync(context, args, CancellationToken.None);
        return new CommandResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task<CommandResult> RunAutoLoadAsync(RepoCommand repoCommand)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(stdout, stderr, new DirectoryInfo(Path.GetTempPath()), services: null, CancellationToken.None);

        var exitCode = await repoCommand.AutoLoadTrustedReposAsync(context, CancellationToken.None);
        return new CommandResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private static int CountProfileLoadLines(string profilePath, string repoName)
    {
        return File.ReadAllLines(profilePath)
            .Count(line => string.Equals(line.TrimEnd(), RepoProfileService.GetRepoLoadLine(repoName), StringComparison.Ordinal));
    }

    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}
