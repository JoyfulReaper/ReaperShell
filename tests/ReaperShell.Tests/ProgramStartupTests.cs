using ReaperShell;
using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Plugins;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

[Collection("Process state")]
public sealed class ProgramStartupTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReaperShell.ProgramStartupTests", Guid.NewGuid().ToString("N"));
    private readonly string _workspaceRoot = WorkspaceRootResolver.FindWorkspaceRoot();
    private readonly string _stateDirectory;
    private string _originalCurrentDirectory = string.Empty;

    public ProgramStartupTests()
    {
        Directory.CreateDirectory(_root);
        _stateDirectory = Path.Combine(_root, "state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public Task InitializeAsync()
    {
        _originalCurrentDirectory = Environment.CurrentDirectory;
        Environment.CurrentDirectory = _workspaceRoot;
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Environment.CurrentDirectory = _originalCurrentDirectory;

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
    public async Task StartupAutoloadRunsByDefault()
    {
        var repoName = "autoload-default";
        await CreateBuiltRepoAsync(repoName, autoLoad: true);

        var result = await RunProgramAsync(
            "--state-dir",
            _stateDirectory,
            "--command",
            "repo list",
            "--no-profile");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Auto-loading trusted repo 'autoload-default'...", result.StdOut);
        Assert.Contains("autoload-default | local | trusted | autoload=on | loaded", result.StdOut);
    }

    [Fact]
    public async Task StartupAutoloadCanBeSkipped()
    {
        var repoName = "autoload-skipped";
        await CreateBuiltRepoAsync(repoName, autoLoad: true);

        var result = await RunProgramAsync(
            "--state-dir",
            _stateDirectory,
            "--command",
            "repo list",
            "--no-profile",
            "--no-autoload");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Auto-loading trusted repo 'autoload-skipped'...", result.StdOut);
        Assert.Contains("autoload-skipped | local | trusted | autoload=on | unloaded", result.StdOut);
    }

    private async Task CreateBuiltRepoAsync(string repoName, bool autoLoad)
    {
        var repoRoot = Path.Combine(_root, repoName);
        await RepoScaffolder.CreateGeneratedPackAsync(repoName, repoRoot, _workspaceRoot, CancellationToken.None);

        var settings = await ShellSettings.LoadOrCreateAsync(_stateDirectory, CancellationToken.None);
        settings.Repos[repoName] = new CommandRepoSettings
        {
            Name = repoName,
            Source = repoRoot,
            LocalPath = repoRoot,
            Trusted = true,
            AutoLoad = autoLoad
        };
        await settings.SaveAsync(_stateDirectory, CancellationToken.None);

        var commandPackManager = new CommandPackManager(new CommandRegistry(), new ProcessRunner(), _workspaceRoot);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(stdout, stderr, new DirectoryInfo(repoRoot), services: null, CancellationToken.None);

        var buildResult = await commandPackManager.BuildAsync(
            settings.Repos[repoName],
            settings.DefaultConfiguration,
            context,
            CancellationToken.None);

        Assert.Equal(0, buildResult.ExitCode);
    }

    private static async Task<ProgramResult> RunProgramAsync(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exitCode = await Program.Main(args);
            return new ProgramResult(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private sealed record ProgramResult(int ExitCode, string StdOut, string StdErr);
}
