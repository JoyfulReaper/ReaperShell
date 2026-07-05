using System.Diagnostics;
using System.Text.Json;
using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Plugins;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class RepoPublishTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReaperShell.RepoPublishTests", Guid.NewGuid().ToString("N"));
    private readonly string _helperRoot = Path.Combine(Path.GetTempPath(), "ReaperShell.RepoPublishTests.gh", Guid.NewGuid().ToString("N"));
    private string _ghExecutablePath = string.Empty;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_helperRoot);

        var helperProjectPath = Path.Combine(_helperRoot, "gh-helper.csproj");
        var helperSourcePath = Path.Combine(_helperRoot, "Program.cs");

        await File.WriteAllTextAsync(
            helperProjectPath,
            """
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>gh</AssemblyName>
  </PropertyGroup>

</Project>
""");

        await File.WriteAllTextAsync(
            helperSourcePath,
            """
using System.Text.Json;

var captureFile = Environment.GetEnvironmentVariable("GH_CAPTURE_FILE");
if (!string.IsNullOrWhiteSpace(captureFile))
{
    await File.WriteAllTextAsync(
        captureFile,
        JsonSerializer.Serialize(new
        {
            cwd = Directory.GetCurrentDirectory(),
            args = args.ToArray()
        }));
}

Console.Out.WriteLine("GH_HELPER_STDOUT");
Console.Error.WriteLine("GH_HELPER_STDERR");
return 0;
""");

        await RunProcessAsync("dotnet", ["build", helperProjectPath, "--nologo"], _helperRoot);
        _ghExecutablePath = Path.Combine(_helperRoot, "bin", "Debug", "net10.0", OperatingSystem.IsWindows() ? "gh.exe" : "gh");
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

        try
        {
            if (Directory.Exists(_helperRoot))
            {
                Directory.Delete(_helperRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task UsageValidationRejectsMissingArguments()
    {
        var result = await RunPublishAsync("publish");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: repo publish <name> <owner/repo> [--private|--public]", result.StdErr);
    }

    [Fact]
    public async Task UnknownRepoIsRejected()
    {
        var result = await RunPublishAsync("publish", "missing", "octocat/widget");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Repo 'missing' is not registered.", result.StdErr);
    }

    [Fact]
    public async Task AlreadyGitRepoIsRejected()
    {
        var settings = await CreateSettingsAsync("local-repo", isGitRepo: true);
        var result = await RunPublishAsync(settings, "publish", "local-repo", "octocat/widget");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("already Git-backed", result.StdErr);
    }

    [Fact]
    public async Task MissingLocalPathIsRejected()
    {
        var settings = await CreateSettingsAsync("missing-repo", localPath: Path.Combine(_root, "missing"));
        var result = await RunPublishAsync(settings, "publish", "missing-repo", "octocat/widget");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Repo path does not exist", result.StdErr);
    }

    [Fact]
    public async Task UnknownVisibilityFlagIsRejected()
    {
        var settings = await CreateSettingsAsync("local-repo");
        var result = await RunPublishAsync(settings, "publish", "local-repo", "octocat/widget", "--draft");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option: --draft", result.StdErr);
    }

    [Fact]
    public async Task PublishBuildsGhCommandAndUpdatesRepoSettings()
    {
        var settings = await CreateSettingsAsync("local-repo");
        var captureFile = Path.Combine(_root, "gh-capture.json");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalCapture = Environment.GetEnvironmentVariable("GH_CAPTURE_FILE");

        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(_ghExecutablePath)! + Path.PathSeparator + originalPath);
            Environment.SetEnvironmentVariable("GH_CAPTURE_FILE", captureFile);

            var result = await RunPublishAsync(settings, "publish", "local-repo", "octocat/widget", "--public");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Published repo 'local-repo' to octocat/widget.", result.StdOut);
            Assert.Contains("repo status local-repo", result.StdOut);
            Assert.Contains("repo save local-repo \"message\"", result.StdOut);
            Assert.True(settings.Repos["local-repo"].IsGitRepo);
            Assert.Equal("octocat/widget", settings.Repos["local-repo"].Source);

            var capture = JsonDocument.Parse(await File.ReadAllTextAsync(captureFile));
            Assert.Equal(new[]
            {
                "repo",
                "create",
                "octocat/widget",
                "--source",
                settings.Repos["local-repo"].LocalPath,
                "--remote",
                "origin",
                "--push",
                "--public"
            }, capture.RootElement.GetProperty("args").EnumerateArray().Select(element => element.GetString()).ToArray());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("GH_CAPTURE_FILE", originalCapture);
        }
    }

    [Fact]
    public async Task MissingGhPrintsClearError()
    {
        var settings = await CreateSettingsAsync("local-repo");
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", _helperRoot);
            var result = await RunPublishAsync(settings, "publish", "local-repo", "octocat/widget");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("GitHub CLI 'gh' was not found. Install and authenticate it first.", result.StdErr);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    private async Task<CommandResult> RunPublishAsync(params string[] args)
    {
        var settings = await CreateSettingsAsync("local-repo");
        return await RunPublishAsync(settings, args);
    }

    private async Task<CommandResult> RunPublishAsync(ShellSettings settings, params string[] args)
    {
        var repoCommand = CreateRepoCommand(settings);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(stdout, stderr, new DirectoryInfo(_root), services: null, CancellationToken.None);

        var exitCode = await repoCommand.ExecuteAsync(context, args, CancellationToken.None);
        return new CommandResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private RepoCommand CreateRepoCommand(ShellSettings settings)
    {
        var sessionState = new ShellSessionState();
        var processRunner = new ProcessRunner(sessionState);
        var registry = new CommandRegistry();
        var parser = new CommandParser();
        var host = new ShellHost(parser, registry, new ShellLifetime(), processRunner, settings, _root, sessionState);
        var watchService = new ShellWatchService(host);
        var commandPackManager = new CommandPackManager(registry, processRunner);
        return new RepoCommand(settings, processRunner, commandPackManager, host, watchService, _root, _root);
    }

    private async Task<ShellSettings> CreateSettingsAsync(string repoName, string? localPath = null, bool isGitRepo = false)
    {
        var repoRoot = localPath ?? Path.Combine(_root, repoName);
        if (localPath is null)
        {
            Directory.CreateDirectory(repoRoot);
            await new CommandPackManifest
            {
                Id = repoName,
                Name = $"{repoName} Pack",
                Description = $"Generated repo '{repoName}'.",
                CommandsPath = "commands"
            }.SaveAsync(Path.Combine(repoRoot, "shellpack.json"), CancellationToken.None);
        }

        var settings = new ShellSettings();
        settings.Repos[repoName] = new CommandRepoSettings
        {
            Name = repoName,
            Source = repoRoot,
            LocalPath = repoRoot,
            Trusted = true,
            IsGitRepo = isGitRepo
        };

        return settings;
    }

    private static async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{fileName}'.");
        }

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"'{fileName}' exited with {process.ExitCode}.");
        }
    }

    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}
