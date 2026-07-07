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
    private readonly string _workspaceRoot = WorkspaceRootResolver.FindWorkspaceRoot();
    private string _ghExecutablePath = string.Empty;
    private string _gitExecutablePath = string.Empty;

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
    await File.AppendAllTextAsync(
        captureFile,
        JsonSerializer.Serialize(new
        {
            cwd = Directory.GetCurrentDirectory(),
            args = args.ToArray()
        }) + Environment.NewLine);
}

var exitCodeText = Environment.GetEnvironmentVariable("GH_EXIT_CODE");
if (!string.IsNullOrWhiteSpace(exitCodeText) && int.TryParse(exitCodeText, out var exitCode) && exitCode != 0)
{
    var message = Environment.GetEnvironmentVariable("GH_FAIL_MESSAGE");
    if (!string.IsNullOrWhiteSpace(message))
    {
        Console.Error.WriteLine(message);
    }
    else
    {
        Console.Error.WriteLine("GH_HELPER_FAILURE");
    }

    return exitCode;
}

Console.Out.WriteLine("GH_HELPER_STDOUT");
Console.Error.WriteLine("GH_HELPER_STDERR");
return 0;
""");

        var buildResult = await RunProcessAsync("dotnet", ["build", helperProjectPath, "--nologo"], _helperRoot);
        if (buildResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to build gh helper: {buildResult.StdOut}\n{buildResult.StdErr}");
        }
        _ghExecutablePath = Path.Combine(_helperRoot, "bin", "Debug", "net10.0", OperatingSystem.IsWindows() ? "gh.exe" : "gh");

        if (!ExternalCommandResolver.TryResolveExecutable("git", out _gitExecutablePath))
        {
            throw new InvalidOperationException("Git was not found on PATH, so RepoPublishTests cannot run.");
        }
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
        Assert.Contains("repo push", result.StdErr);
        Assert.Contains("repo save", result.StdErr);
        Assert.False(File.Exists(Path.Combine(settings.Repos["local-repo"].LocalPath, ".gitignore")));
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
    public async Task PublishBootstrapsNonGitPackBeforeGh()
    {
        var settings = await CreateSettingsAsync("local-repo");
        var repoRoot = settings.Repos["local-repo"].LocalPath;
        Directory.CreateDirectory(Path.Combine(repoRoot, "bin", "Debug", "net10.0"));
        Directory.CreateDirectory(Path.Combine(repoRoot, "obj", "Debug", "net10.0"));
        await File.WriteAllTextAsync(Path.Combine(repoRoot, "bin", "Debug", "net10.0", "artifact.dll"), "binary");
        await File.WriteAllTextAsync(Path.Combine(repoRoot, "obj", "Debug", "net10.0", "artifact.obj"), "obj");

        var captureFile = Path.Combine(_root, "gh-capture-non-git.jsonl");
        using var environment = CreatePublishEnvironmentScope(captureFile);

        var result = await RunPublishAsync(settings, "publish", "local-repo", "octocat/widget", "--public");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Published repo 'local-repo' to octocat/widget.", result.StdOut);
        Assert.True(settings.Repos["local-repo"].IsGitRepo);
        Assert.Equal("octocat/widget", settings.Repos["local-repo"].Source);
        Assert.True(Directory.Exists(Path.Combine(repoRoot, ".git")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".gitignore")));
        Assert.Equal("1", (await RunGitAsync(repoRoot, "rev-list", "--count", "HEAD")).StdOut.Trim());

        var trackedFiles = (await RunGitAsync(repoRoot, "ls-files")).StdOut;
        Assert.Contains(".gitignore", trackedFiles, StringComparison.Ordinal);
        Assert.DoesNotContain("bin/Debug/net10.0/artifact.dll", trackedFiles, StringComparison.Ordinal);
        Assert.DoesNotContain("obj/Debug/net10.0/artifact.obj", trackedFiles, StringComparison.Ordinal);

        var capture = ReadCaptureLines(captureFile);
        Assert.Single(capture);
        Assert.Equal(new[]
        {
            "repo",
            "create",
            "octocat/widget",
            "--source",
            repoRoot,
            "--push",
            "--public",
            "--remote",
            "origin"
        }, capture[0].args);
    }

    [Fact]
    public async Task PublishBootstrapsExistingGitDirWithoutCommits()
    {
        var repoRoot = await CreatePackRootAsync("git-no-commit");
        await RunGitAsync(repoRoot, "init");
        var settings = await CreateSettingsAsync("git-no-commit", repoRoot);

        var captureFile = Path.Combine(_root, "gh-capture-no-commit.jsonl");
        using var environment = CreatePublishEnvironmentScope(captureFile);

        var result = await RunPublishAsync(settings, "publish", "git-no-commit", "octocat/widget");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Reinitialized existing Git repository", result.StdOut + result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1", (await RunGitAsync(repoRoot, "rev-list", "--count", "HEAD")).StdOut.Trim());

        var capture = ReadCaptureLines(captureFile);
        Assert.Single(capture);
        Assert.Equal(new[]
        {
            "repo",
            "create",
            "octocat/widget",
            "--source",
            repoRoot,
            "--push",
            "--private",
            "--remote",
            "origin"
        }, capture[0].args);
    }

    [Fact]
    public async Task PublishDoesNotReinitializeCommittedPack()
    {
        var repoRoot = await CreatePackRootAsync("git-with-commit");
        await RunGitAsync(repoRoot, "init");
        await RunGitAsync(repoRoot, "add", ".");
        await RunGitAsync(repoRoot, "commit", "-m", "Seed commit");
        var commitBeforePublish = (await RunGitAsync(repoRoot, "rev-parse", "HEAD")).StdOut.Trim();
        var settings = await CreateSettingsAsync("git-with-commit", repoRoot);

        var captureFile = Path.Combine(_root, "gh-capture-with-commit.jsonl");
        using var environment = CreatePublishEnvironmentScope(captureFile);

        var result = await RunPublishAsync(settings, "publish", "git-with-commit", "octocat/widget", "--public");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Reinitialized existing Git repository", result.StdOut + result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(commitBeforePublish, (await RunGitAsync(repoRoot, "rev-parse", "HEAD")).StdOut.Trim());

        var capture = ReadCaptureLines(captureFile);
        Assert.Single(capture);
        Assert.Equal(new[]
        {
            "repo",
            "create",
            "octocat/widget",
            "--source",
            repoRoot,
            "--push",
            "--public",
            "--remote",
            "origin"
        }, capture[0].args);
    }

    [Fact]
    public async Task PublishLeavesLocalSettingsUnchangedWhenGhFails()
    {
        var settings = await CreateSettingsAsync("local-repo");
        using var environment = CreatePublishEnvironmentScope(
            Path.Combine(_root, "gh-fail.jsonl"),
            ghExitCode: "5",
            ghFailMessage: "GH_AUTH_REQUIRED");

        var result = await RunPublishAsync(settings, "publish", "local-repo", "octocat/widget");

        Assert.Equal(5, result.ExitCode);
        Assert.Contains("GH_AUTH_REQUIRED", result.StdErr);
        Assert.False(settings.Repos["local-repo"].IsGitRepo);
        Assert.Equal(settings.Repos["local-repo"].LocalPath, settings.Repos["local-repo"].Source);
    }

    [Fact]
    public async Task MissingGitFailsClearly()
    {
        var settings = await CreateSettingsAsync("local-repo");
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(_ghExecutablePath));
            var result = await RunPublishAsync(settings, "publish", "local-repo", "octocat/widget");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Git 'git' was not found. Install it first.", result.StdErr);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
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

    [Fact]
    public async Task ExistingOriginPointingElsewhereFailsClearly()
    {
        var repoRoot = await CreatePackRootAsync("origin-mismatch");
        await RunGitAsync(repoRoot, "init");
        await RunGitAsync(repoRoot, "add", ".");
        await RunGitAsync(repoRoot, "commit", "-m", "Seed commit");
        await RunGitAsync(repoRoot, "remote", "add", "origin", "https://example.com/other/repo.git");
        var settings = await CreateSettingsAsync("origin-mismatch", repoRoot);

        var captureFile = Path.Combine(_root, "gh-origin-mismatch.jsonl");
        using var environment = CreatePublishEnvironmentScope(captureFile);

        var result = await RunPublishAsync(settings, "publish", "origin-mismatch", "octocat/widget");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("origin remote", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(captureFile));
        Assert.False(settings.Repos["origin-mismatch"].IsGitRepo);
    }

    [Fact]
    public async Task ExistingOriginPointingToRequestedRepoIsAccepted()
    {
        var repoRoot = await CreatePackRootAsync("origin-match");
        await RunGitAsync(repoRoot, "init");
        await RunGitAsync(repoRoot, "add", ".");
        await RunGitAsync(repoRoot, "commit", "-m", "Seed commit");
        await RunGitAsync(repoRoot, "remote", "add", "origin", "https://github.com/JoyfulReaper/reapershell-iis-tools.git");
        var settings = await CreateSettingsAsync("origin-match", repoRoot);

        var captureFile = Path.Combine(_root, "gh-origin-match.jsonl");
        using var environment = CreatePublishEnvironmentScope(captureFile);

        var result = await RunPublishAsync(settings, "publish", "origin-match", "JoyfulReaper/reapershell-iis-tools", "--private");

        Assert.Equal(0, result.ExitCode);
        var capture = ReadCaptureLines(captureFile);
        Assert.Single(capture);
        Assert.Equal(new[]
        {
            "repo",
            "create",
            "JoyfulReaper/reapershell-iis-tools",
            "--source",
            repoRoot,
            "--push",
            "--private"
        }, capture[0].args);
        Assert.True(settings.Repos["origin-match"].IsGitRepo);
        Assert.Equal("JoyfulReaper/reapershell-iis-tools", settings.Repos["origin-match"].Source);
    }

    [Fact]
    public async Task StatusReportsHalfState()
    {
        var repoRoot = await CreatePackRootAsync("half-state");
        await RunGitAsync(repoRoot, "init");
        var settings = await CreateSettingsAsync("half-state", repoRoot);

        var result = await RunStatusAsync(settings, "status", "half-state");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("has a local .git directory", result.StdOut);
        Assert.Contains("Use `repo publish <name> <owner/repo>` to finish publishing it", result.StdOut);
        Assert.Contains("update/remove/re-add the repo", result.StdOut);
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

    private async Task<CommandResult> RunStatusAsync(ShellSettings settings, params string[] args)
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
        var commandPackManager = new CommandPackManager(registry, processRunner, _workspaceRoot);
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

    private async Task<string> CreatePackRootAsync(string repoName)
    {
        var repoRoot = Path.Combine(_root, repoName);
        Directory.CreateDirectory(repoRoot);
        await new CommandPackManifest
        {
            Id = repoName,
            Name = $"{repoName} Pack",
            Description = $"Generated repo '{repoName}'.",
            CommandsPath = "commands"
        }.SaveAsync(Path.Combine(repoRoot, "shellpack.json"), CancellationToken.None);
        return repoRoot;
    }

    private async Task<CommandResult> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        return await RunProcessAsync(_gitExecutablePath, arguments, workingDirectory, CreateGitIdentityEnvironment());
    }

    private static Dictionary<string, string?> CreateGitIdentityEnvironment()
    {
        return new Dictionary<string, string?>
        {
            ["GIT_AUTHOR_NAME"] = "ReaperShell Test",
            ["GIT_AUTHOR_EMAIL"] = "reapershell-test@example.com",
            ["GIT_COMMITTER_NAME"] = "ReaperShell Test",
            ["GIT_COMMITTER_EMAIL"] = "reapershell-test@example.com"
        };
    }

    private static string PrependPath(string entry, string? originalPath)
    {
        return string.IsNullOrWhiteSpace(originalPath)
            ? entry
            : entry + Path.PathSeparator + originalPath;
    }

    private EnvironmentScope CreatePublishEnvironmentScope(
        string captureFile,
        string? ghExitCode = null,
        string? ghFailMessage = null)
    {
        var scope = new EnvironmentScope()
            .Set("PATH", PrependPath(Path.GetDirectoryName(_ghExecutablePath)!, Environment.GetEnvironmentVariable("PATH")))
            .Set("GH_CAPTURE_FILE", captureFile)
            .Set("GIT_AUTHOR_NAME", "ReaperShell Test")
            .Set("GIT_AUTHOR_EMAIL", "reapershell-test@example.com")
            .Set("GIT_COMMITTER_NAME", "ReaperShell Test")
            .Set("GIT_COMMITTER_EMAIL", "reapershell-test@example.com");

        if (ghExitCode is not null)
        {
            scope.Set("GH_EXIT_CODE", ghExitCode);
        }

        if (ghFailMessage is not null)
        {
            scope.Set("GH_FAIL_MESSAGE", ghFailMessage);
        }

        return scope;
    }

    private static IReadOnlyList<(string cwd, string[] args)> ReadCaptureLines(string captureFile)
    {
        return File.ReadAllLines(captureFile)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                var cwd = document.RootElement.GetProperty("cwd").GetString() ?? string.Empty;
                var args = document.RootElement.GetProperty("args").EnumerateArray().Select(element => element.GetString() ?? string.Empty).ToArray();
                return (cwd, args);
            })
            .ToArray();
    }

    private static async Task<CommandResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environmentVariables is not null)
        {
            foreach (var pair in environmentVariables)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{fileName}'.");
        }

        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new CommandResult(process.ExitCode, standardOutput, standardError);
    }

    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly List<(string Name, string? Value)> _values = [];

        public EnvironmentScope Set(string name, string? value)
        {
            if (_values.All(entry => !string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                _values.Add((name, Environment.GetEnvironmentVariable(name)));
            }

            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            for (var index = _values.Count - 1; index >= 0; index--)
            {
                Environment.SetEnvironmentVariable(_values[index].Name, _values[index].Value);
            }
        }
    }
}
