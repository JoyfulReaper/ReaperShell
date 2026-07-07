using System.Diagnostics;
using System.Text.Json;
using ReaperShell;
using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

[Collection("Process state")]
public sealed class ExternalCommandFallbackTests : IAsyncLifetime
{
    private readonly string _helperRoot = Path.Combine(Path.GetTempPath(), "ReaperShell.ExternalFallbackTests", Guid.NewGuid().ToString("N"));
    private string _helperExecutablePath = string.Empty;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_helperRoot);
        var helperProjectPath = Path.Combine(_helperRoot, "fixture-command.csproj");
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
    <AssemblyName>fixture-command</AssemblyName>
  </PropertyGroup>

</Project>
""");

        await File.WriteAllTextAsync(
            helperSourcePath,
            """
using System.Text.Json;

var resultPath = args[0];
var payload = new
{
    cwd = Directory.GetCurrentDirectory(),
    args = args.Skip(1).ToArray()
};

File.WriteAllText(resultPath, JsonSerializer.Serialize(payload));
Console.Out.WriteLine("EXTERNAL_STDOUT");
Console.Error.WriteLine("EXTERNAL_STDERR");
return 17;
""");

        await RunProcessAsync("dotnet", ["build", helperProjectPath, "--nologo"], _helperRoot);
        _helperExecutablePath = Path.Combine(_helperRoot, "bin", "Debug", "net10.0", OperatingSystem.IsWindows() ? "fixture-command.exe" : "fixture-command");
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_helperRoot))
        {
            Directory.Delete(_helperRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task BuiltInCommandWinsOverExternalExecutable()
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var helperDirectory = Path.GetDirectoryName(_helperExecutablePath)!;
            Environment.SetEnvironmentVariable("PATH", helperDirectory + Path.PathSeparator + originalPath);

            var resultFile = Path.Combine(_helperRoot, "should-not-exist.json");
            var (exitCode, stdout, stderr) = await RunShellCommandAsync(
                "fixture-command",
                new ShellSettings { ExternalCommandMode = ExternalCommandMode.PathOnly },
                registry =>
                {
                    registry.RegisterBuiltIn(new ConstantCommand("fixture-command", "builtin-wins"));
                },
                "ignored");

            Assert.Equal(0, exitCode);
            Assert.Contains("builtin-wins", stdout);
            Assert.DoesNotContain("EXTERNAL_STDOUT", stdout);
            Assert.False(File.Exists(resultFile));
            Assert.True(string.IsNullOrWhiteSpace(stderr));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public async Task PluginCommandWinsOverExternalExecutable()
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var helperDirectory = Path.GetDirectoryName(_helperExecutablePath)!;
            Environment.SetEnvironmentVariable("PATH", helperDirectory + Path.PathSeparator + originalPath);

            var (exitCode, stdout, stderr) = await RunShellCommandAsync(
                "fixture-command",
                new ShellSettings { ExternalCommandMode = ExternalCommandMode.PathOnly },
                registry =>
                {
                    registry.RegisterPlugin(new ConstantCommand("fixture-command", "plugin-wins"), "test-pack", "test-pack-path");
                });

            Assert.Equal(0, exitCode);
            Assert.Contains("plugin-wins", stdout);
            Assert.DoesNotContain("external executable", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrWhiteSpace(stderr));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public async Task DisabledExternalFallbackReportsUnknownCommand()
    {
        var (exitCode, stdout, stderr) = await RunShellCommandAsync(
            "missing-command",
            new ShellSettings { ExternalCommandMode = ExternalCommandMode.Disabled },
            registry: null);

        Assert.Equal(1, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("Unknown command: missing-command", stderr);
        Assert.Contains("disabled", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PathOnlyRunsExternalExecutableWithArgumentsAndWorkingDirectory()
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var resultFile = Path.Combine(_helperRoot, "result.json");
        try
        {
            var helperDirectory = Path.GetDirectoryName(_helperExecutablePath)!;
            Environment.SetEnvironmentVariable("PATH", helperDirectory + Path.PathSeparator + originalPath);

            var workingDirectory = Path.Combine(_helperRoot, "cwd");
            Directory.CreateDirectory(workingDirectory);

            var (exitCode, stdout, stderr) = await RunShellCommandAsync(
                "fixture-command",
                new ShellSettings { ExternalCommandMode = ExternalCommandMode.PathOnly },
                registry: null,
                workingDirectory: workingDirectory,
                arguments: [resultFile, "first", "second"]);

            Assert.Equal(17, exitCode);
            Assert.Contains("EXTERNAL_STDOUT", stdout);
            Assert.Contains("EXTERNAL_STDERR", stderr);

            var payload = JsonDocument.Parse(await File.ReadAllTextAsync(resultFile));
            Assert.Equal(workingDirectory, payload.RootElement.GetProperty("cwd").GetString());
            Assert.Equal(new[] { "first", "second" }, payload.RootElement.GetProperty("args").EnumerateArray().Select(element => element.GetString()).ToArray());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public async Task PathOnlyReportsUnknownCommandWhenExecutableIsMissing()
    {
        var (exitCode, stdout, stderr) = await RunShellCommandAsync(
            "definitely-not-on-path",
            new ShellSettings { ExternalCommandMode = ExternalCommandMode.PathOnly },
            registry: null);

        Assert.Equal(1, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("Unknown command: definitely-not-on-path", stderr);
        Assert.Contains("was found on PATH", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhichReportsPathOnlyExecutableAsRunnable()
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var helperDirectory = Path.GetDirectoryName(_helperExecutablePath)!;
            Environment.SetEnvironmentVariable("PATH", helperDirectory + Path.PathSeparator + originalPath);

            var (exitCode, stdout, stderr) = await RunBuiltInCommandAsync(
                new WhichCommand(new ShellSettings { ExternalCommandMode = ExternalCommandMode.PathOnly }, new CommandRegistry()),
                "fixture-command");

            Assert.Equal(0, exitCode);
            Assert.Contains("external executable ->", stdout);
            Assert.Contains("external command mode -> PathOnly", stdout);
            Assert.Contains("runnable -> yes", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("fixture-command", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrWhiteSpace(stderr));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public async Task WhichReportsDisabledExternalExecutableAsNotRunnable()
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var helperDirectory = Path.GetDirectoryName(_helperExecutablePath)!;
            Environment.SetEnvironmentVariable("PATH", helperDirectory + Path.PathSeparator + originalPath);

            var (exitCode, stdout, stderr) = await RunBuiltInCommandAsync(
                new WhichCommand(new ShellSettings { ExternalCommandMode = ExternalCommandMode.Disabled }, new CommandRegistry()),
                "fixture-command");

            Assert.Equal(0, exitCode);
            Assert.Contains("external executable ->", stdout);
            Assert.Contains("external command mode -> Disabled", stdout);
            Assert.Contains("runnable -> no, external command fallback is disabled", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrWhiteSpace(stderr));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public async Task DescribeReportsPathOnlyExecutableAsRunnable()
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var helperDirectory = Path.GetDirectoryName(_helperExecutablePath)!;
            Environment.SetEnvironmentVariable("PATH", helperDirectory + Path.PathSeparator + originalPath);

            var (exitCode, stdout, stderr) = await RunBuiltInCommandAsync(
                new DescribeCommand(new ShellSettings { ExternalCommandMode = ExternalCommandMode.PathOnly }, new CommandRegistry()),
                "fixture-command");

            Assert.Equal(0, exitCode);
            Assert.Contains("NAME: fixture-command", stdout);
            Assert.Contains("SOURCE: external", stdout);
            Assert.Contains($"PATH: {_helperExecutablePath}", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("EXTERNAL COMMAND MODE: PathOnly", stdout);
            Assert.Contains("RUNNABLE: Yes", stdout);
            Assert.True(string.IsNullOrWhiteSpace(stderr));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public async Task DescribeReportsDisabledExternalExecutableAsNotRunnable()
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var helperDirectory = Path.GetDirectoryName(_helperExecutablePath)!;
            Environment.SetEnvironmentVariable("PATH", helperDirectory + Path.PathSeparator + originalPath);

            var (exitCode, stdout, stderr) = await RunBuiltInCommandAsync(
                new DescribeCommand(new ShellSettings { ExternalCommandMode = ExternalCommandMode.Disabled }, new CommandRegistry()),
                "fixture-command");

            Assert.Equal(0, exitCode);
            Assert.Contains("NAME: fixture-command", stdout);
            Assert.Contains("SOURCE: external", stdout);
            Assert.Contains($"PATH: {_helperExecutablePath}", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("EXTERNAL COMMAND MODE: Disabled", stdout);
            Assert.Contains("RUNNABLE: No, external command fallback is disabled", stdout);
            Assert.True(string.IsNullOrWhiteSpace(stderr));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunBuiltInCommandAsync(
        IShellCommand command,
        params string[] args)
    {
        var registry = new CommandRegistry();
        registry.RegisterBuiltIn(command);
        return await ExecuteCommandAsync(
            command.Name,
            new ShellSettings(),
            registry,
            args);
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunShellCommandAsync(
        string commandText,
        ShellSettings settings,
        Action<CommandRegistry>? registry,
        string? workingDirectory = null,
        params string[] arguments)
    {
        var commandRegistry = new CommandRegistry();
        registry?.Invoke(commandRegistry);
        return await ExecuteCommandAsync(
            commandText,
            settings,
            commandRegistry,
            arguments,
            workingDirectory);
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> ExecuteCommandAsync(
        string commandName,
        ShellSettings settings,
        CommandRegistry registry,
        IReadOnlyList<string> commandArgs,
        string? workingDirectory = null)
    {
        var parser = new CommandParser();
        var host = new ShellHost(parser, registry, new ShellLifetime(), new ProcessRunner(), settings, _helperRoot);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(
            stdout,
            stderr,
            new DirectoryInfo(workingDirectory ?? _helperRoot),
            services: null,
            CancellationToken.None);

        var commandText = commandArgs.Count == 0
            ? commandName
            : commandName + " " + string.Join(" ", commandArgs.Select(QuoteIfNeeded));

        var exitCode = await host.RunAutomationCommandAsync(context, commandText, echoCommand: false, CancellationToken.None);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
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

    private sealed class ConstantCommand : IShellCommand
    {
        private readonly string _name;
        private readonly string _output;

        public ConstantCommand(string name, string output)
        {
            _name = name;
            _output = output;
        }

        public string Name => _name;

        public string Description => "Test built-in command.";

        public Task<int> ExecuteAsync(
            ShellContext context,
            IReadOnlyList<string> args,
            CancellationToken cancellationToken = default)
        {
            context.WriteLine(_output);
            return Task.FromResult(0);
        }
    }
}
