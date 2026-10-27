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

if (args[0] == "--pipe-emit")
{
    Console.Out.WriteLine("alpha");
    Console.Out.WriteLine("beta");
    Console.Error.WriteLine("PIPE_STDERR");
    return 17;
}

if (args[0] == "--pipe-upper")
{
    Console.Out.Write((await Console.In.ReadToEndAsync()).ToUpperInvariant());
    Console.Error.WriteLine("PIPE_STDERR");
    return 0;
}

if (args[0] == "--pipe-first")
{
    Console.Out.WriteLine("early exit");
    return 23;
}

if (args[0] == "--pipe-pressure")
{
    Console.Out.WriteLine(new string('o', 256 * 1024));
    Console.Error.WriteLine(new string('e', 256 * 1024));
    Console.Out.WriteLine((await Console.In.ReadToEndAsync()).Length);
    return 0;
}

if (args[0] is "--pipe-block" or "--pipe-wait")
{
    if (args[0] == "--pipe-wait")
    {
        await Console.In.ReadToEndAsync();
    }

    Console.Error.WriteLine($"READY:{Environment.ProcessId}");
    await Task.Delay(TimeSpan.FromSeconds(30));
    return 0;
}

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

    [Theory]
    [InlineData(">", "EXTERNAL_STDOUT", "", "EXTERNAL_STDERR")]
    [InlineData("2>", "EXTERNAL_STDERR", "EXTERNAL_STDOUT", "")]
    public async Task ExternalRedirectionPreservesArgumentsStreamsAndExitCode(
        string operation, string fileContent, string expectedStdout, string expectedStderr)
    {
        var resultFile = Path.Combine(_helperRoot, "result.json");
        var outputPath = Path.Combine(_helperRoot, "output.txt");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        (int exitCode, string stdout, string stderr) result;
        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(_helperExecutablePath) + Path.PathSeparator + originalPath);
            result = await RunShellCommandAsync(
                $"fixture-command \"{resultFile}\" first {operation}output.txt",
                new ShellSettings { ExternalCommandMode = ExternalCommandMode.PathOnly },
                registry: null);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }

        Assert.Equal(17, result.exitCode);
        Assert.Equal(expectedStdout, result.stdout.TrimEnd('\r', '\n'));
        Assert.Equal(expectedStderr, result.stderr.TrimEnd('\r', '\n'));
        Assert.Equal(fileContent + Environment.NewLine, await File.ReadAllTextAsync(outputPath));
        using var payload = JsonDocument.Parse(await File.ReadAllTextAsync(resultFile));
        Assert.Equal(new[] { "first" }, payload.RootElement.GetProperty("args").EnumerateArray().Select(element => element.GetString()).ToArray());
        using (File.Open(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }

        File.Delete(outputPath);
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

    [Theory]
    [InlineData("echo hello | fixture-command --pipe-upper", "HELLO", 1)]
    [InlineData("plugin-producer | fixture-command --pipe-upper", "PLUGIN", 1)]
    [InlineData("fixture-command --pipe-emit | cat", "alpha\nbeta", 1)]
    [InlineData("fixture-command --pipe-emit | plugin-consumer", "alpha\nbeta", 1)]
    [InlineData("fixture-command --pipe-emit | fixture-command --pipe-upper", "ALPHA\nBETA", 2)]
    [InlineData("echo hello | fixture-command --pipe-upper | cat", "HELLO", 1)]
    public async Task PipelinesConnectBuiltInsPluginsAndExternalCommands(string input, string expected, int errorLines)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = await RunPipelineAsync(input, timeout.Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StdOut.Replace("\r\n", "\n").TrimEnd('\n'));
        Assert.Equal(string.Concat(Enumerable.Repeat("PIPE_STDERR" + Environment.NewLine, errorLines)), result.StdErr);
    }

    [Fact]
    public async Task ExternalConsumerMayExitBeforeReadingAllPipelineInput()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = await RunPipelineAsync("big-input | fixture-command --pipe-first", timeout.Token);

        Assert.Equal(23, result.ExitCode);
        Assert.Equal("early exit" + Environment.NewLine, result.StdOut);
        Assert.Empty(result.StdErr);
    }

    [Fact]
    public async Task ExternalOutputIsDrainedWhileWritingLargePipelineInput()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = await RunPipelineAsync("big-input | fixture-command --pipe-pressure | plugin-consumer", timeout.Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new string('o', 256 * 1024) + Environment.NewLine +
            (1024 * 1024 + Environment.NewLine.Length) + Environment.NewLine, result.StdOut);
        Assert.Equal(new string('e', 256 * 1024) + Environment.NewLine, result.StdErr);
    }

    [Theory]
    [InlineData("--pipe-block")]
    [InlineData("--pipe-wait")]
    public async Task PipelineCancellationStopsExternalProcessAndSkipsLaterStages(string mode)
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        using var cancellation = new CancellationTokenSource();
        using var stderr = new ReadyWriter();
        Process? child = null;
        Task<int>? execution = null;
        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(_helperExecutablePath) + Path.PathSeparator + originalPath);
            var registry = CreatePipelineRegistry();
            var host = new ShellHost(new CommandParser(), registry, new ShellLifetime(), new ProcessRunner(),
                new ShellSettings { ExternalCommandMode = ExternalCommandMode.PathOnly }, _helperRoot);
            var context = new ShellContext(new StringWriter(), stderr, new DirectoryInfo(_helperRoot), null, cancellation.Token);
            execution = host.RunAutomationCommandAsync(context,
                $"big-input | fixture-command {mode} | echo should-not-run >later.txt", false, cancellation.Token);
            var pid = await stderr.Ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            child = Process.GetProcessById(pid);

            cancellation.Cancel();

            Assert.Equal(1, await execution.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(child.HasExited);
            Assert.False(File.Exists(Path.Combine(_helperRoot, "later.txt")));
            Assert.Contains("Pipeline canceled", stderr.ToString());
        }
        finally
        {
            cancellation.Cancel();
            Environment.SetEnvironmentVariable("PATH", originalPath);
            if (child is not null && !child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }

            child?.Dispose();
            if (execution is not null)
            {
                await execution.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunPipelineAsync(string input, CancellationToken cancellationToken)
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(_helperExecutablePath) + Path.PathSeparator + originalPath);
            return await ExecuteCommandAsync(input,
                new ShellSettings { ExternalCommandMode = ExternalCommandMode.PathOnly },
                CreatePipelineRegistry(), [], cancellationToken: cancellationToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    private CommandRegistry CreatePipelineRegistry()
    {
        var registry = new CommandRegistry();
        registry.RegisterBuiltIn(new EchoCommand());
        registry.RegisterBuiltIn(new CatCommand());
        registry.RegisterPlugin(new ConstantCommand("plugin-producer", "plugin"), "test-pack", _helperRoot);
        registry.RegisterPlugin(new InputCommand(), "test-pack", _helperRoot);
        registry.RegisterPlugin(new ConstantCommand("big-input", new string('x', 1024 * 1024)), "test-pack", _helperRoot);
        return registry;
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
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
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
            cancellationToken);

        var commandText = commandArgs.Count == 0
            ? commandName
            : commandName + " " + string.Join(" ", commandArgs.Select(QuoteIfNeeded));

        var exitCode = await host.RunAutomationCommandAsync(context, commandText, echoCommand: false, cancellationToken);
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

    private sealed class ReadyWriter : StringWriter
    {
        public TaskCompletionSource<int> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (value is not null && value.StartsWith("READY:", StringComparison.Ordinal))
            {
                Ready.TrySetResult(int.Parse(value[6..]));
            }
        }
    }

    private sealed class InputCommand : IShellCommand
    {
        public string Name => "plugin-consumer";
        public string Description => "Copies pipeline input.";

        public async Task<int> ExecuteAsync(ShellContext context, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        {
            await context.Out.WriteAsync(await context.Input.ReadToEndAsync(cancellationToken));
            return 0;
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
