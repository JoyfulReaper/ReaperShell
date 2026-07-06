using System.Diagnostics;
using System.Text.Json;
using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Plugins;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class EditCommandTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReaperShell.EditCommandTests", Guid.NewGuid().ToString("N"));
    private readonly string _helperRoot = Path.Combine(Path.GetTempPath(), "ReaperShell.EditCommandTests.editor", Guid.NewGuid().ToString("N"));
    private string _helperExecutablePath = string.Empty;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_helperRoot);

        var helperProjectPath = Path.Combine(_helperRoot, "editor-helper.csproj");
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
    <AssemblyName>edit-helper</AssemblyName>
  </PropertyGroup>

</Project>
""");

        await File.WriteAllTextAsync(
            helperSourcePath,
            """
using System.Text.Json;

var captureFile = Environment.GetEnvironmentVariable("EDIT_CAPTURE_FILE");
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

return 0;
""");

        await RunProcessAsync("dotnet", ["build", helperProjectPath, "--nologo"], _helperRoot);
        _helperExecutablePath = Path.Combine(_helperRoot, "bin", "Debug", "net10.0", OperatingSystem.IsWindows() ? "edit-helper.exe" : "edit-helper");
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
    public async Task EditPathStillWorks()
    {
        var workingDirectory = Path.Combine(_root, "cwd");
        Directory.CreateDirectory(workingDirectory);
        var filePath = Path.Combine(workingDirectory, "note.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        var result = await RunEditAsync(
            CreateSettings(),
            workingDirectory,
            "note.txt");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Path.GetFullPath(filePath), result.TargetPath);
    }

    [Fact]
    public async Task EditRepoOpensRepoRoot()
    {
        var settings = CreateSettings();
        var repoRoot = await CreateRepoAsync("sample-repo");
        settings.Repos["sample-repo"] = new CommandRepoSettings
        {
            Name = "sample-repo",
            Source = repoRoot,
            LocalPath = repoRoot,
            Trusted = true
        };

        var result = await RunEditAsync(settings, _root, "--repo", "sample-repo");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(repoRoot, result.TargetPath);
    }

    [Fact]
    public async Task EditRepoCommandOpensCommandDirectory()
    {
        var settings = CreateSettings();
        var repoRoot = await CreateRepoAsync("command-repo");
        var commandDirectory = Path.Combine(repoRoot, "commands", "hello");
        Directory.CreateDirectory(commandDirectory);
        settings.Repos["command-repo"] = new CommandRepoSettings
        {
            Name = "command-repo",
            Source = repoRoot,
            LocalPath = repoRoot,
            Trusted = true
        };

        var result = await RunEditAsync(settings, _root, "--repo", "command-repo", "--command", "hello");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(commandDirectory, result.TargetPath);
    }

    [Fact]
    public async Task EditRepoOptionsAreOrderIndependent()
    {
        var settings = CreateSettings();
        var repoRoot = await CreateRepoAsync("ordered-repo");
        var commandDirectory = Path.Combine(repoRoot, "commands", "hello");
        Directory.CreateDirectory(commandDirectory);
        settings.Repos["ordered-repo"] = new CommandRepoSettings
        {
            Name = "ordered-repo",
            Source = repoRoot,
            LocalPath = repoRoot,
            Trusted = true
        };

        var result = await RunEditAsync(settings, _root, "--command", "hello", "--repo", "ordered-repo");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(commandDirectory, result.TargetPath);
    }

    [Fact]
    public async Task EditUnknownRepoFailsClearly()
    {
        var result = await RunEditAsync(CreateSettings(), _root, "--repo", "missing");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Repo 'missing' is not registered.", result.StdErr);
    }

    [Fact]
    public async Task EditMissingCommandDirectoryFailsClearly()
    {
        var settings = CreateSettings();
        var repoRoot = await CreateRepoAsync("missing-command-repo");
        settings.Repos["missing-command-repo"] = new CommandRepoSettings
        {
            Name = "missing-command-repo",
            Source = repoRoot,
            LocalPath = repoRoot,
            Trusted = true
        };

        var result = await RunEditAsync(settings, _root, "--repo", "missing-command-repo", "--command", "hello");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Command directory does not exist:", result.StdErr);
    }

    [Fact]
    public async Task EditUnknownOptionFailsClearly()
    {
        var result = await RunEditAsync(CreateSettings(), _root, "--bogus");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option: --bogus", result.StdErr);
        Assert.Contains("Usage: edit <path>", result.StdErr);
    }

    [Fact]
    public async Task EditMissingOptionValueFailsClearly()
    {
        var result = await RunEditAsync(CreateSettings(), _root, "--repo");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Missing value for --repo.", result.StdErr);
    }

    private ShellSettings CreateSettings()
    {
        return new ShellSettings
        {
            EditorCommand = _helperExecutablePath
        };
    }

    private async Task<string> CreateRepoAsync(string repoName)
    {
        var repoRoot = Path.Combine(_root, repoName);
        Directory.CreateDirectory(Path.Combine(repoRoot, "commands"));

        await new CommandPackManifest
        {
            Id = repoName,
            Name = $"{repoName} Pack",
            Description = $"Generated repo '{repoName}'.",
            CommandsPath = "commands"
        }.SaveAsync(Path.Combine(repoRoot, "shellpack.json"), CancellationToken.None);

        return repoRoot;
    }

    private async Task<EditResult> RunEditAsync(
        ShellSettings settings,
        string workingDirectory,
        params string[] args)
    {
        var captureFile = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json");
        var originalCapture = Environment.GetEnvironmentVariable("EDIT_CAPTURE_FILE");
        Environment.SetEnvironmentVariable("EDIT_CAPTURE_FILE", captureFile);

        try
        {
            var editCommand = new EditCommand(settings, new EditorLauncher(settings, new ProcessRunner()));
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var context = new ShellContext(stdout, stderr, new DirectoryInfo(workingDirectory), services: null, CancellationToken.None);

            var exitCode = await editCommand.ExecuteAsync(context, args, CancellationToken.None);
            var targetPath = await TryReadCaptureAsync(captureFile);

            return new EditResult(exitCode, stdout.ToString(), stderr.ToString(), targetPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("EDIT_CAPTURE_FILE", originalCapture);
        }
    }

    private static async Task<string?> TryReadCaptureAsync(string captureFile)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (File.Exists(captureFile))
            {
                try
                {
                    await using var stream = new FileStream(
                        captureFile,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite);
                    var document = await JsonDocument.ParseAsync(stream);
                    return document.RootElement.GetProperty("args").EnumerateArray().Select(element => element.GetString()).FirstOrDefault();
                }
                catch (IOException)
                {
                    // The helper may still be writing the capture file; retry briefly.
                }
            }

            await Task.Delay(50);
        }

        return null;
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
            throw new InvalidOperationException($"'{fileName}' exited with code {process.ExitCode}.");
        }
    }

    private sealed record EditResult(int ExitCode, string StdOut, string StdErr, string? TargetPath);
}
