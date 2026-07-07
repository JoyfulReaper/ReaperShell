using System.Diagnostics;
using System.Text.Json;
using ReaperShell.Abstractions;
using ReaperShell;
using ReaperShell.BuiltIns;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

[Collection("Process state")]
public sealed class SessionConvenienceCommandsTests : IAsyncLifetime
{
    private readonly string _helperRoot = Path.Combine(Path.GetTempPath(), "ReaperShell.SessionConvenienceCommandsTests", Guid.NewGuid().ToString("N"));
    private string _helperExecutablePath = string.Empty;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_helperRoot);
        var helperProjectPath = Path.Combine(_helperRoot, "session-helper.csproj");
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
    <AssemblyName>session-helper</AssemblyName>
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
    args = args.Skip(1).ToArray(),
    value = Environment.GetEnvironmentVariable("RSH_SESSION_TEST")
};

File.WriteAllText(resultPath, JsonSerializer.Serialize(payload));
Console.Out.WriteLine("SESSION_HELPER_STDOUT");
Console.Error.WriteLine("SESSION_HELPER_STDERR");
return 0;
""");

        await RunProcessAsync("dotnet", ["build", helperProjectPath, "--nologo"], _helperRoot);
        _helperExecutablePath = Path.Combine(_helperRoot, "bin", "Debug", "net10.0", OperatingSystem.IsWindows() ? "session-helper.exe" : "session-helper");
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
    public async Task VersionPrintsRuntimeInformation()
    {
        var (exitCode, stdout, stderr) = await ExecuteBuiltInAsync(new VersionCommand(), []);

        Assert.Equal(0, exitCode);
        Assert.Contains("REAPER SHELL VERSION:", stdout);
        Assert.Contains(".NET RUNTIME:", stdout);
        Assert.Contains("OS DESCRIPTION:", stdout);
        Assert.Contains("PROCESS ARCHITECTURE:", stdout);
        Assert.Contains("PROCESS ID:", stdout);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public async Task HistoryRecordsCommandsAndCanBeCleared()
    {
        var sessionState = new ShellSessionState();
        var (host, context) = CreateHost(sessionState, new EchoCommand(), new HistoryCommand(sessionState));

        await RunHostCommandAsync(host, context, "echo one");
        await RunHostCommandAsync(host, context, "echo one");

        Assert.Equal(new[] { "echo one" }, sessionState.GetHistory());

        var history = await RunHostCommandAsync(host, context, "history");
        Assert.Contains("1: echo one", history.StdOut);
        Assert.DoesNotContain("2: echo one", history.StdOut);

        await RunHostCommandAsync(host, context, "history clear");
        Assert.Empty(sessionState.GetHistory());
    }

    [Fact]
    public async Task EnvListsOverridesAndFallsBackToInheritedProcessEnvironment()
    {
        var sessionState = new ShellSessionState();
        var envCommand = new EnvCommand(sessionState);
        var originalValue = Environment.GetEnvironmentVariable("RSH_SESSION_TEST");

        try
        {
            var set = await ExecuteBuiltInAsync(envCommand, ["set", "RSH_SESSION_TEST", "session-value"]);
            Assert.Equal(0, set.ExitCode);
            Assert.Contains("RSH_SESSION_TEST=session-value", set.StdOut);

            var listed = await ExecuteBuiltInAsync(envCommand, []);
            Assert.Equal(0, listed.ExitCode);
            Assert.Contains("RSH_SESSION_TEST=session-value", listed.StdOut);

            var getSession = await ExecuteBuiltInAsync(envCommand, ["get", "RSH_SESSION_TEST"]);
            Assert.Equal(0, getSession.ExitCode);
            Assert.Contains("RSH_SESSION_TEST=session-value", getSession.StdOut);

            var unset = await ExecuteBuiltInAsync(envCommand, ["unset", "RSH_SESSION_TEST"]);
            Assert.Equal(0, unset.ExitCode);
            Assert.Contains("Removed session override", unset.StdOut);
            Assert.Contains("Inherited OS environment values remain unchanged", unset.StdOut);

            Environment.SetEnvironmentVariable("RSH_SESSION_TEST", "inherited-value");

            var getInherited = await ExecuteBuiltInAsync(envCommand, ["get", "RSH_SESSION_TEST"]);
            Assert.Equal(0, getInherited.ExitCode);
            Assert.Contains("(inherited)", getInherited.StdOut);
            Assert.Contains("RSH_SESSION_TEST=inherited-value", getInherited.StdOut);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RSH_SESSION_TEST", originalValue);
        }
    }

    [Fact]
    public async Task EnvUnsetUsageMentionsSessionOverrideOnly()
    {
        var envCommand = new EnvCommand(new ShellSessionState());
        var (exitCode, stdout, stderr) = await ExecuteBuiltInAsync(envCommand, ["unset"]);

        Assert.Equal(1, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stdout));
        Assert.Contains("session override only", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessRunnerAppliesSessionEnvironmentToChildProcess()
    {
        var sessionState = new ShellSessionState();
        sessionState.SetEnvironmentVariable("RSH_SESSION_TEST", "from-session");

        var runner = new ProcessRunner(sessionState);
        var workingDirectory = Path.Combine(_helperRoot, "cwd");
        Directory.CreateDirectory(workingDirectory);
        var resultFile = Path.Combine(_helperRoot, "result.json");

        var result = await runner.RunAsync(
            _helperExecutablePath,
            [resultFile, "first", "second"],
            workingDirectory);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("SESSION_HELPER_STDOUT", result.StandardOutput);
        Assert.Contains("SESSION_HELPER_STDERR", result.StandardError);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(resultFile));
        Assert.Equal(workingDirectory, document.RootElement.GetProperty("cwd").GetString());
        Assert.Equal("from-session", document.RootElement.GetProperty("value").GetString());
        Assert.Equal(new[] { "first", "second" }, document.RootElement.GetProperty("args").EnumerateArray().Select(element => element.GetString()).ToArray());
    }

    [Fact]
    public async Task ReloadReappliesSettingsFromDisk()
    {
        var stateDirectory = Path.Combine(_helperRoot, "state");
        Directory.CreateDirectory(stateDirectory);

        var settings = new ShellSettings();
        await settings.SaveAsync(stateDirectory, CancellationToken.None);

        var sessionState = new ShellSessionState();
        var parser = new CommandParser();
        var registry = new CommandRegistry();
        var processRunner = new ProcessRunner(sessionState);
        var host = new ShellHost(parser, registry, new ShellLifetime(), processRunner, settings, stateDirectory, sessionState);
        registry.RegisterBuiltIn(new EchoCommand());
        registry.RegisterBuiltIn(new AliasCommand(settings, registry, stateDirectory));
        registry.RegisterBuiltIn(new ReloadCommand(host));

        var beforeReload = await RunHostCommandAsync(host, CreateContext(stateDirectory), "echo before");
        Assert.Contains("before", beforeReload.StdOut);

        var updatedSettings = new ShellSettings();
        updatedSettings.Aliases["hi"] = "echo hello";
        await updatedSettings.SaveAsync(stateDirectory, CancellationToken.None);

        var reload = await RunHostCommandAsync(host, CreateContext(stateDirectory), "reload");
        Assert.Contains("Reloaded settings.", reload.StdOut);

        var alias = await RunHostCommandAsync(host, CreateContext(stateDirectory), "hi");
        Assert.Contains("hello", alias.StdOut);
    }

    private (ShellHost Host, ShellContext Context) CreateHost(ShellSessionState sessionState, params IShellCommand[] commandRegistrations)
    {
        var parser = new CommandParser();
        var registry = new CommandRegistry();
        var processRunner = new ProcessRunner(sessionState);
        var stateDirectory = Path.Combine(_helperRoot, "history-state");
        Directory.CreateDirectory(stateDirectory);
        var settings = new ShellSettings();
        var host = new ShellHost(parser, registry, new ShellLifetime(), processRunner, settings, stateDirectory, sessionState);
        foreach (var registration in commandRegistrations)
        {
            registry.RegisterBuiltIn(registration);
        }

        return (host, CreateContext(stateDirectory));
    }

    private ShellContext CreateContext(string workingDirectory)
    {
        return new ShellContext(
            new StringWriter(),
            new StringWriter(),
            new DirectoryInfo(workingDirectory),
            services: null,
            CancellationToken.None);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> ExecuteBuiltInAsync(
        IShellCommand command,
        IReadOnlyList<string> args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(stdout, stderr, new DirectoryInfo(Path.GetTempPath()), services: null, CancellationToken.None);
        var exitCode = await command.ExecuteAsync(context, args, CancellationToken.None);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunHostCommandAsync(
        ShellHost host,
        ShellContext context,
        string commandText)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var runContext = new ShellContext(stdout, stderr, context.WorkingDirectory, services: null, CancellationToken.None);
        var exitCode = await host.RunCommandAsync(runContext, commandText, CancellationToken.None);
        return (exitCode, stdout.ToString(), stderr.ToString());
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
}
