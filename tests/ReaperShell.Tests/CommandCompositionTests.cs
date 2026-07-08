using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class CommandCompositionParserTests
{
    private readonly ShellCommandLineParser _parser = new();

    [Fact]
    public void ParsesSimpleCommandAsOneCommand()
    {
        Assert.True(_parser.TryParse("echo hello", out var commandLine, out var error));
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.True(commandLine!.IsSimpleCommand);
        Assert.Equal(["echo", "hello"], commandLine.Pipelines[0].Segments[0].Tokens);
    }

    [Fact]
    public void QuotedPipeDoesNotSplit()
    {
        Assert.True(_parser.TryParse("echo \"a | b\"", out var commandLine, out var error));
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(["echo", "a | b"], commandLine!.Pipelines[0].Segments[0].Tokens);
    }

    [Theory]
    [InlineData("echo hi > out.txt", CommandRedirectionKind.StdoutOverwrite, "out.txt")]
    [InlineData("echo hi >> out.txt", CommandRedirectionKind.StdoutAppend, "out.txt")]
    [InlineData("missing 2> err.txt", CommandRedirectionKind.StderrOverwrite, "err.txt")]
    [InlineData("missing 2>> err.txt", CommandRedirectionKind.StderrAppend, "err.txt")]
    [InlineData("doctor *> all.txt", CommandRedirectionKind.CombinedOverwrite, "all.txt")]
    [InlineData("echo hi | grep h", null, null)]
    [InlineData("echo hi && echo ok", null, null)]
    [InlineData("echo hi || echo nope", null, null)]
    public void ParsesCompositionSyntax(
        string input,
        CommandRedirectionKind? expectedRedirectionKind,
        string? expectedTarget)
    {
        Assert.True(_parser.TryParse(input, out var commandLine, out var error));
        Assert.True(string.IsNullOrWhiteSpace(error));

        commandLine = commandLine!;
        if (input.Contains("&&", StringComparison.Ordinal) || input.Contains("||", StringComparison.Ordinal))
        {
            Assert.Equal(2, commandLine.Pipelines.Count);
            Assert.NotNull(commandLine.Pipelines[0].NextOperator);
        }

        if (input.Contains("|", StringComparison.Ordinal) && !input.Contains("&&", StringComparison.Ordinal) && !input.Contains("||", StringComparison.Ordinal))
        {
            Assert.Equal(2, commandLine.Pipelines[0].Segments.Count);
        }

        if (expectedRedirectionKind is not null)
        {
            var redirection = commandLine.Pipelines[0].Segments[0].Redirections.Single();
            Assert.Equal(expectedRedirectionKind, redirection.Kind);
            Assert.Equal(expectedTarget, redirection.TargetPath);
        }
    }

    [Fact]
    public void MissingRedirectionTargetFails()
    {
        Assert.False(_parser.TryParse("echo hi >", out _, out var error));
        Assert.Contains("Missing redirection target", error);
    }

    [Fact]
    public void EmptyPipelineSegmentFails()
    {
        Assert.False(_parser.TryParse("echo hi |", out _, out var error));
        Assert.Contains("Pipeline segment cannot be empty", error);
    }

    [Fact]
    public void UnsupportedOperatorFails()
    {
        Assert.False(_parser.TryParse("echo hi & echo bye", out _, out var error));
        Assert.Contains("Unsupported shell operator: '&'.", error);
    }
}

public sealed class CommandCompositionExecutionTests
{
    [Fact]
    public async Task StdoutRedirectionWritesToFile()
    {
        using var temp = new TempDirectory();
        var result = await RunAutomationAsync(temp.Directory, "echo hello > out.txt");

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StdOut));
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr));
        Assert.Equal($"hello{Environment.NewLine}", await File.ReadAllTextAsync(temp.GetPath("out.txt")));
    }

    [Fact]
    public async Task StdoutAppendRedirectionAppends()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(temp.GetPath("out.txt"), "first" + Environment.NewLine);

        var result = await RunAutomationAsync(temp.Directory, "echo second >> out.txt");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"first{Environment.NewLine}second{Environment.NewLine}", await File.ReadAllTextAsync(temp.GetPath("out.txt")));
        Assert.True(string.IsNullOrWhiteSpace(result.StdOut));
    }

    [Fact]
    public async Task StderrRedirectionWritesToFile()
    {
        using var temp = new TempDirectory();
        var result = await RunAutomationAsync(temp.Directory, "stderr-only 2> err.txt");

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StdOut));
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr));
        Assert.Equal($"boom{Environment.NewLine}", await File.ReadAllTextAsync(temp.GetPath("err.txt")));
    }

    [Fact]
    public async Task CombinedRedirectionCapturesStdoutAndStderr()
    {
        using var temp = new TempDirectory();
        var result = await RunAutomationAsync(temp.Directory, "chatty *> all.txt");

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StdOut));
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr));
        var contents = await File.ReadAllTextAsync(temp.GetPath("all.txt"));
        Assert.Contains("stdout-hello", contents);
        Assert.Contains("stderr-oops", contents);
    }

    [Fact]
    public async Task PipelineFeedsStdoutToNextCommand()
    {
        using var temp = new TempDirectory();
        var result = await RunAutomationAsync(temp.Directory, "echo hello | grep hell");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StdOut);
    }

    [Fact]
    public async Task PipelineReturnsNonZeroWhenNothingMatches()
    {
        using var temp = new TempDirectory();
        var result = await RunAutomationAsync(temp.Directory, "echo hello | grep nope");

        Assert.Equal(1, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [Fact]
    public async Task BufferedPipelineChainsMultipleCommands()
    {
        using var temp = new TempDirectory();
        await File.WriteAllLinesAsync(
            temp.GetPath("app.log"),
            [
                "info",
                "2026-07-07T12:00:01Z error one",
                "2026-07-07T12:00:02Z error two"
            ]);

        var result = await RunAutomationAsync(temp.Directory, "cat app.log | grep error | head -n 1");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("error one", result.StdOut);
        Assert.DoesNotContain("error two", result.StdOut);
    }

    [Fact]
    public async Task AndAlsoSkipsSecondCommandOnFailure()
    {
        using var temp = new TempDirectory();
        var result = await RunAutomationAsync(temp.Directory, "failcmd && echo no");

        Assert.Equal(7, result.ExitCode);
        Assert.DoesNotContain("no", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OrElseRunsSecondCommandOnFailure()
    {
        using var temp = new TempDirectory();
        var result = await RunAutomationAsync(temp.Directory, "failcmd || echo yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("yes", result.StdOut);
    }

    [Fact]
    public async Task HistoryRecordsOriginalComposedCommandOnce()
    {
        using var temp = new TempDirectory();
        var sessionState = new ShellSessionState();
        var host = CreateHost(temp.Directory, sessionState);
        var context = CreateContext(temp.Directory);

        var input = "echo hello | grep hell && echo done";
        var exitCode = await host.RunCommandAsync(context, input, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal([input], sessionState.GetHistory());
    }

    [Fact]
    public async Task HooksRunOnceForComposedCommand()
    {
        using var temp = new TempDirectory();
        var stateDirectory = GetStateDirectory(temp.Directory);
        var ritualsDirectory = Path.Combine(stateDirectory, "rituals");
        Directory.CreateDirectory(ritualsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(ritualsDirectory, "after.rsh"),
            "echo hook >> hook.log");

        var settings = new ShellSettings();
        settings.Hooks[ShellHookEventNames.AfterCommand] = ["after"];
        var host = CreateHost(temp.Directory, new ShellSessionState(), settings);
        var context = CreateContext(temp.Directory);

        var exitCode = await host.RunCommandAsync(context, "echo one | cat | cat", CancellationToken.None);

        Assert.Equal(0, exitCode);
        var hookLog = await File.ReadAllLinesAsync(temp.GetPath("hook.log"));
        Assert.Single(hookLog);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunAutomationAsync(
        DirectoryInfo workingDirectory,
        string commandText)
    {
        var host = CreateHost(workingDirectory, new ShellSessionState());
        var context = CreateContext(workingDirectory);
        var exitCode = await host.RunAutomationCommandAsync(context, commandText, echoCommand: false, CancellationToken.None);
        return (exitCode, Normalize(context.Out), Normalize(context.Error));
    }

    private static ShellHost CreateHost(
        DirectoryInfo workingDirectory,
        ShellSessionState sessionState,
        ShellSettings? settings = null,
        params IShellCommand[] commandRegistrations)
    {
        var parser = new CommandParser();
        var registry = new CommandRegistry();
        var processRunner = new ProcessRunner(sessionState);
        var shellSettings = settings ?? new ShellSettings();
        var stateDirectory = Path.Combine(workingDirectory.FullName, ".rsh");
        Directory.CreateDirectory(stateDirectory);
        var host = new ShellHost(parser, registry, new ShellLifetime(), processRunner, shellSettings, stateDirectory, sessionState);

        registry.RegisterBuiltIn(new EchoCommand());
        registry.RegisterBuiltIn(new CatCommand());
        registry.RegisterBuiltIn(new GrepCommand());
        registry.RegisterBuiltIn(new HeadCommand());
        registry.RegisterBuiltIn(new TailCommand());
        registry.RegisterBuiltIn(new FailCommand());
        registry.RegisterBuiltIn(new StdErrCommand());
        registry.RegisterBuiltIn(new ChattyCommand());

        foreach (var command in commandRegistrations)
        {
            registry.RegisterBuiltIn(command);
        }

        return host;
    }

    private static ShellContext CreateContext(DirectoryInfo workingDirectory)
    {
        return new ShellContext(
            new StringWriter(),
            new StringWriter(),
            workingDirectory,
            services: null,
            CancellationToken.None);
    }

    private static string GetStateDirectory(DirectoryInfo workingDirectory)
    {
        return Path.Combine(workingDirectory.FullName, ".rsh");
    }

    private static string Normalize(TextWriter writer)
    {
        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\r', '\n');
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "ReaperShell.CommandCompositionTests", Guid.NewGuid().ToString("N")));
            Directory.Create();
        }

        public DirectoryInfo Directory { get; }

        public string GetPath(params string[] parts)
        {
            return Path.Combine([Directory.FullName, .. parts]);
        }

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

    private sealed class FailCommand : IShellCommand
    {
        public string Name => "failcmd";

        public string Description => "Always fails.";

        public Task<int> ExecuteAsync(ShellContext context, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        {
            context.WriteErrorLine("fail");
            return Task.FromResult(7);
        }
    }

    private sealed class StdErrCommand : IShellCommand
    {
        public string Name => "stderr-only";

        public string Description => "Writes only to stderr.";

        public Task<int> ExecuteAsync(ShellContext context, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        {
            context.WriteErrorLine("boom");
            return Task.FromResult(0);
        }
    }

    private sealed class ChattyCommand : IShellCommand
    {
        public string Name => "chatty";

        public string Description => "Writes to stdout and stderr.";

        public Task<int> ExecuteAsync(ShellContext context, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        {
            context.WriteLine("stdout-hello");
            context.WriteErrorLine("stderr-oops");
            return Task.FromResult(0);
        }
    }
}
