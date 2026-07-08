using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class ScriptVariableExpansionTests
{
    [Theory]
    [InlineData("ritual run greet Kyle Debug")]
    [InlineData("ritual run greet --continue-on-error Kyle Debug")]
    [InlineData("ritual run greet Kyle Debug --continue-on-error")]
    public async Task RitualArgsExpandVariablesAndIgnoreContinueFlag(string commandText)
    {
        using var harness = ScriptTestHarness.Create();
        var ritualPath = Path.Combine(harness.StateDirectory, "rituals", "greet.rsh");
        await File.WriteAllTextAsync(
            ritualPath,
            """
echo script:$0
echo hello $1
echo config $2
echo all args: $*
echo previous:$?
""");

        var result = await harness.RunAutomationAsync(commandText);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"script:{ritualPath}", result.StdOut);
        Assert.Contains("hello Kyle", result.StdOut);
        Assert.Contains("config Debug", result.StdOut);
        Assert.Contains("all args: Kyle Debug", result.StdOut);
        Assert.Contains("previous:0", result.StdOut);
        Assert.DoesNotContain("--continue-on-error", result.StdOut);
    }

    [Fact]
    public async Task MissingAndUnknownVariablesExpandToEmptyStrings()
    {
        using var harness = ScriptTestHarness.Create();
        var scriptPath = harness.WriteScript(
            """
echo missing:$3
echo unknown:$does_not_exist
echo $$1
echo cost $$5
""");

        var result = await harness.RunScriptAsync(scriptPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("missing:", result.StdOut);
        Assert.Contains("unknown:", result.StdOut);
        Assert.Contains("$1", result.StdOut);
        Assert.Contains("cost $5", result.StdOut);
    }

    [Fact]
    public async Task SessionEnvironmentOverridesExpandInScripts()
    {
        using var harness = ScriptTestHarness.Create();
        var scriptPath = harness.WriteScript(
            """
env set project ReaperShell
echo $project
echo ${project}
""");

        var result = await harness.RunScriptAsync(scriptPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, CountLinesExactly(result.StdOut, "ReaperShell"));
    }

    [Fact]
    public async Task LastExitCodeTracksSuccessAndFailureAcrossCommands()
    {
        using var harness = ScriptTestHarness.Create();

        var successScriptPath = harness.WriteScript(
            """
echo ok
echo previous:$?
""");

        var success = await harness.RunScriptAsync(successScriptPath);
        Assert.Equal(0, success.ExitCode);
        Assert.Contains("previous:0", success.StdOut);

        var failureScriptPath = harness.WriteScript(
            """
failcmd
echo previous:$?
""");

        var failure = await harness.RunScriptAsync(failureScriptPath, continueOnError: true);
        Assert.Equal(1, failure.ExitCode);
        Assert.Contains("previous:1", failure.StdOut);
    }

    [Fact]
    public async Task PositionalArgsCanBeUsedInRedirectionTargets()
    {
        using var harness = ScriptTestHarness.Create();
        var ritualPath = Path.Combine(harness.StateDirectory, "rituals", "write-file.rsh");
        await File.WriteAllTextAsync(
            ritualPath,
            """
echo hello > $1.txt
""");

        var result = await harness.RunAutomationAsync("ritual run write-file out");

        Assert.Equal(0, result.ExitCode);
        var outputPath = Path.Combine(harness.WorkingDirectory.FullName, "out.txt");
        Assert.True(File.Exists(outputPath));
        Assert.Equal($"hello{Environment.NewLine}", await File.ReadAllTextAsync(outputPath));
    }

    [Fact]
    public async Task PipelinesStillWorkAfterExpansion()
    {
        using var harness = ScriptTestHarness.Create();
        var ritualPath = Path.Combine(harness.StateDirectory, "rituals", "pipe.rsh");
        await File.WriteAllTextAsync(
            ritualPath,
            """
echo $1 | grep $2
""");

        var result = await harness.RunAutomationAsync("ritual run pipe hello ell");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StdOut);
    }

    private static int CountLinesExactly(string value, string match)
    {
        return value
            .Split([Environment.NewLine], StringSplitOptions.None)
            .Count(line => string.Equals(line, match, StringComparison.Ordinal));
    }
}

internal sealed class ScriptTestHarness : IDisposable
{
    private readonly TempDirectory _tempDirectory = new();
    private readonly ShellSessionState _sessionState = new();
    private readonly CommandRegistry _registry = new();
    private readonly ShellHost _host;

    private ScriptTestHarness()
    {
        StateDirectory = Path.Combine(_tempDirectory.Directory.FullName, ".rsh");
        Directory.CreateDirectory(Path.Combine(StateDirectory, "rituals"));

        var settings = new ShellSettings();
        _host = new ShellHost(
            new CommandParser(),
            _registry,
            new ShellLifetime(),
            new ProcessRunner(_sessionState),
            settings,
            StateDirectory,
            _sessionState);

        _registry.RegisterBuiltIn(new EchoCommand());
        _registry.RegisterBuiltIn(new EnvCommand(_sessionState));
        _registry.RegisterBuiltIn(new GrepCommand());
        _registry.RegisterBuiltIn(new FailCommand());
        _registry.RegisterBuiltIn(new RitualCommand(_host, StateDirectory));
    }

    public DirectoryInfo WorkingDirectory => _tempDirectory.Directory;

    public string StateDirectory { get; }

    public static ScriptTestHarness Create()
    {
        return new ScriptTestHarness();
    }

    public string WriteScript(string contents, string? fileName = null)
    {
        fileName ??= $"{Guid.NewGuid():N}.rsh";
        var path = Path.Combine(WorkingDirectory.FullName, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    public async Task<(int ExitCode, string StdOut, string StdErr)> RunScriptAsync(
        string scriptPath,
        bool continueOnError = false)
    {
        var context = CreateContext();
        var exitCode = await _host.RunScriptAsync(context, scriptPath, continueOnError, CancellationToken.None);
        return (exitCode, ((StringWriter)context.Out).ToString(), ((StringWriter)context.Error).ToString());
    }

    public async Task<(int ExitCode, string StdOut, string StdErr)> RunAutomationAsync(string commandText)
    {
        var context = CreateContext();
        var exitCode = await _host.RunAutomationCommandAsync(context, commandText, echoCommand: false, CancellationToken.None);
        return (exitCode, ((StringWriter)context.Out).ToString(), ((StringWriter)context.Error).ToString());
    }

    private ShellContext CreateContext()
    {
        return new ShellContext(
            new StringWriter(),
            new StringWriter(),
            WorkingDirectory,
            services: null,
            CancellationToken.None);
    }

    public void Dispose()
    {
        _tempDirectory.Dispose();
    }

    private sealed class FailCommand : IShellCommand
    {
        public string Name => "failcmd";

        public string Description => "Always fails.";

        public Task<int> ExecuteAsync(ShellContext context, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        {
            context.WriteErrorLine("fail");
            return Task.FromResult(1);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "ReaperShell.ScriptVariableExpansionTests", Guid.NewGuid().ToString("N")));
            Directory.Create();
        }

        public DirectoryInfo Directory { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists)
                {
                    Directory.Delete(recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
