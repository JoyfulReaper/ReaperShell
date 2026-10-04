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

    [Theory]
    [InlineData("echo *", new[] { "echo", "*" })]
    [InlineData("echo *.log", new[] { "echo", "*.log" })]
    [InlineData("echo *bot*", new[] { "echo", "*bot*" })]
    [InlineData("grep error logs/*.txt", new[] { "grep", "error", "logs/*.txt" })]
    [InlineData("iis-error-search --iis-log *bot*", new[] { "iis-error-search", "--iis-log", "*bot*" })]
    public void BareStarAndGlobLikeArgumentsParseAsWords(string input, string[] expectedTokens)
    {
        Assert.True(_parser.TryParse(input, out var commandLine, out var error));
        Assert.True(string.IsNullOrWhiteSpace(error));

        Assert.Equal(expectedTokens, commandLine!.Pipelines[0].Segments[0].Tokens);
    }

    [Fact]
    public void CombinedOverwriteStillParses()
    {
        Assert.True(_parser.TryParse("doctor *> all.txt", out var commandLine, out var error));
        Assert.True(string.IsNullOrWhiteSpace(error));

        var redirection = commandLine!.Pipelines[0].Segments[0].Redirections.Single();
        Assert.Equal(CommandRedirectionKind.CombinedOverwrite, redirection.Kind);
        Assert.Equal("all.txt", redirection.TargetPath);
    }

    [Fact]
    public void CombinedAppendStillParses()
    {
        Assert.True(_parser.TryParse("doctor *>> all.txt", out var commandLine, out var error));
        Assert.True(string.IsNullOrWhiteSpace(error));

        var redirection = commandLine!.Pipelines[0].Segments[0].Redirections.Single();
        Assert.Equal(CommandRedirectionKind.CombinedAppend, redirection.Kind);
        Assert.Equal("all.txt", redirection.TargetPath);
    }

    [Theory]
    [InlineData("echo hi >")]
    [InlineData("echo hi >>")]
    [InlineData("echo hi 2>")]
    [InlineData("echo hi > 2> err.txt")]
    [InlineData("echo hi > \"\" tail")]
    [InlineData("echo hi > '' tail")]
    public void MissingRedirectionTargetFails(string input)
    {
        Assert.False(_parser.TryParse(input, out _, out var error));
        Assert.Contains("Missing redirection target", error);
    }

    [Theory]
    [InlineData("echo hello>out.txt", "hello", CommandRedirectionKind.StdoutOverwrite, "out.txt")]
    [InlineData("echo hello>>out.txt", "hello", CommandRedirectionKind.StdoutAppend, "out.txt")]
    [InlineData("echo hello 2>err.txt", "hello", CommandRedirectionKind.StderrOverwrite, "err.txt")]
    [InlineData("echo hello2>out.txt", "hello2", CommandRedirectionKind.StdoutOverwrite, "out.txt")]
    [InlineData("echo \"2\">out.txt", "2", CommandRedirectionKind.StdoutOverwrite, "out.txt")]
    [InlineData("echo hello>\"file with spaces.txt\"", "hello", CommandRedirectionKind.StdoutOverwrite, "file with spaces.txt")]
    public void RedirectionIsSeparatedFromArguments(string input, string argument, CommandRedirectionKind kind, string target)
    {
        Assert.True(_parser.TryParse(input, out var commandLine, out var error), error);
        var segment = Assert.Single(Assert.Single(commandLine!.Pipelines).Segments);
        Assert.Equal(["echo", argument], segment.Tokens);
        Assert.Equal(new CommandRedirection(kind, target), Assert.Single(segment.Redirections));
    }

    [Theory]
    [InlineData("echo \"hello > world\"", "hello > world")]
    [InlineData("echo '2> nope'", "2> nope")]
    [InlineData("echo \"hello >> world\"", "hello >> world")]
    public void QuotedRedirectionOperatorsAreLiteral(string input, string argument)
    {
        Assert.True(_parser.TryParse(input, out var commandLine, out var error), error);
        Assert.True(commandLine!.IsSimpleCommand);
        Assert.Equal(["echo", argument], commandLine.Pipelines[0].Segments[0].Tokens);
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
    [Theory]
    [InlineData(">", false)]
    [InlineData(">", true)]
    [InlineData(">>", false)]
    [InlineData(">>", true)]
    public async Task StdoutRedirectionCreatesTruncatesOrAppends(string operation, bool exists)
    {
        using var temp = new TempDirectory();
        var path = temp.GetPath("file with spaces.txt");
        if (exists)
        {
            await File.WriteAllTextAsync(path, "original content");
        }

        var result = await RunAutomationAsync(temp.Directory, $"echo hello{operation}\"file with spaces.txt\"");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StdOut);
        Assert.Empty(result.StdErr);
        var prefix = exists && operation == ">>" ? "original content" : string.Empty;
        Assert.Equal(prefix + "hello" + Environment.NewLine, await File.ReadAllTextAsync(path));
        AssertFileReleased(path);
    }

    [Theory]
    [InlineData(">", "", "stderr-oops", "stdout-hello")]
    [InlineData("2>", "stdout-hello", "", "stderr-oops")]
    public async Task RedirectionOnlyCapturesSelectedStream(string operation, string stdout, string stderr, string fileContent)
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(temp.GetPath("out.txt"), "old content");

        var result = await RunAutomationAsync(temp.Directory, $"chatty {operation}out.txt");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(stdout, result.StdOut);
        Assert.Equal(stderr, result.StdErr);
        Assert.Equal(fileContent + Environment.NewLine, await File.ReadAllTextAsync(temp.GetPath("out.txt")));
        AssertFileReleased(temp.GetPath("out.txt"));
    }

    [Theory]
    [InlineData("echo \"hello > world\"", "hello > world")]
    [InlineData("echo '2> nope'", "2> nope")]
    [InlineData("echo ordinary command", "ordinary command")]
    [InlineData("echo \"\" ordinary", "ordinary")]
    public async Task LiteralArgumentsRemainUnchanged(string input, string expected)
    {
        using var temp = new TempDirectory();
        var result = await RunAutomationAsync(temp.Directory, input);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StdOut);
        Assert.Empty(result.StdErr);
        Assert.Empty(temp.Directory.GetFiles());
    }

    [Theory]
    [InlineData("echo hello >")]
    [InlineData("echo hello 2>")]
    public async Task MissingTargetReportsCleanError(string input)
    {
        using var temp = new TempDirectory();
        var result = await RunAutomationAsync(temp.Directory, input);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.StdOut);
        Assert.Contains("Missing redirection target", result.StdErr);
    }

    [Fact]
    public async Task OpeningSecondTargetFailureReleasesFirstFileAndReportsToOriginalStderr()
    {
        using var temp = new TempDirectory();
        var result = await RunAutomationAsync(temp.Directory, "chatty > out.txt 2> missing/err.txt");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.StdOut);
        Assert.Contains("Failed to open redirection target", result.StdErr);
        Assert.Contains("err.txt", result.StdErr);
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(temp.GetPath("out.txt")));
        AssertFileReleased(temp.GetPath("out.txt"));
    }

    [Fact]
    public async Task AliasesKeepRedirectionAndOriginalHistory()
    {
        using var temp = new TempDirectory();
        var session = new ShellSessionState();
        var settings = new ShellSettings();
        settings.Aliases["say"] = "echo alias";
        var host = CreateHost(temp.Directory, session, settings);
        var context = CreateContext(temp.Directory);
        const string input = "say hello>out.txt";

        Assert.Equal(0, await host.RunCommandAsync(context, input, ShellRunOptions.Quiet, CancellationToken.None));
        Assert.Equal([input], session.GetHistory());
        Assert.Equal("alias hello" + Environment.NewLine, await File.ReadAllTextAsync(temp.GetPath("out.txt")));
        Assert.Equal(0, await host.RunCommandAsync(context, "echo normal", ShellRunOptions.Quiet, CancellationToken.None));
        Assert.Equal("normal", Normalize(context.Out));
    }

    [Fact]
    public async Task UnknownCommandErrorCanBeRedirected()
    {
        using var temp = new TempDirectory();
        var result = await RunAutomationAsync(temp.Directory, "unknown-command 2>errors.txt");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.StdOut);
        Assert.Empty(result.StdErr);
        Assert.Contains("Unknown command: unknown-command", await File.ReadAllTextAsync(temp.GetPath("errors.txt")));
    }

    [Fact]
    public async Task RedirectedFailurePreservesExitCode()
    {
        using var temp = new TempDirectory();
        var result = await RunAutomationAsync(temp.Directory, "failcmd 2>err.txt");

        Assert.Equal(7, result.ExitCode);
        Assert.Empty(result.StdErr);
        Assert.Equal("fail" + Environment.NewLine, await File.ReadAllTextAsync(temp.GetPath("err.txt")));
        AssertFileReleased(temp.GetPath("err.txt"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrowingOrCanceledCommandReleasesFiles(bool canceled)
    {
        using var temp = new TempDirectory();
        var host = CreateHost(temp.Directory, new ShellSessionState(), null, new ThrowingCommand(canceled));
        var context = CreateContext(temp.Directory);

        Assert.Equal(1, await host.RunAutomationCommandAsync(context, "throws > out.txt 2> err.txt", false, CancellationToken.None));
        Assert.Empty(Normalize(context.Error));
        Assert.Contains(canceled ? "Command canceled" : "Command failed", await File.ReadAllTextAsync(temp.GetPath("err.txt")));
        Assert.Equal("before failure" + Environment.NewLine, await File.ReadAllTextAsync(temp.GetPath("out.txt")));
        AssertFileReleased(temp.GetPath("out.txt"));
        AssertFileReleased(temp.GetPath("err.txt"));
    }

    [Fact]
    public async Task RedirectedPluginRetainsContextAndUsesPlainOutput()
    {
        using var temp = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        var services = new TestServices();
        var context = new ShellContext(new StringWriter(), new StringWriter(), new StringReader("input"),
            temp.Directory, services, cancellation.Token, ShellColorMode.Always);
        var registry = new CommandRegistry();
        var command = new ContextCommand();
        registry.RegisterPlugin(command, "test-pack", temp.Directory.FullName);
        var host = new ShellHost(new CommandParser(), registry, new ShellLifetime(), new ProcessRunner(),
            new ShellSettings(), temp.Directory.FullName);

        Assert.Equal(0, await host.RunAutomationCommandAsync(context, "context >out.txt 2>err.txt", false, cancellation.Token));
        var redirected = Assert.IsType<ShellContext>(command.Context);
        Assert.NotSame(context, redirected);
        Assert.Same(context.WorkingDirectory, redirected.WorkingDirectory);
        Assert.Same(context.Services, redirected.Services);
        Assert.Same(context.Input, redirected.Input);
        Assert.Equal(context.CancellationToken, redirected.CancellationToken);
        Assert.Equal(context.ColorMode, redirected.ColorMode);
        Assert.IsType<StringWriter>(context.Out);
        Assert.IsType<StringWriter>(context.Error);
        Assert.Equal("success" + Environment.NewLine, await File.ReadAllTextAsync(temp.GetPath("out.txt")));
        Assert.Equal("error" + Environment.NewLine, await File.ReadAllTextAsync(temp.GetPath("err.txt")));
    }

    private static void AssertFileReleased(string path)
    {
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }

        File.Delete(path);
        Assert.False(File.Exists(path));
    }

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

    private sealed class TestServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class ContextCommand : IShellCommand
    {
        public string Name => "context";
        public string Description => "Captures context.";
        public ShellContext? Context { get; private set; }

        public Task<int> ExecuteAsync(ShellContext context, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        {
            Context = context;
            context.WriteSuccessLine("success");
            context.WriteErrorLine("error");
            return Task.FromResult(0);
        }
    }

    private sealed class ThrowingCommand(bool canceled) : IShellCommand
    {
        public string Name => "throws";
        public string Description => "Throws after writing output.";

        public Task<int> ExecuteAsync(ShellContext context, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        {
            context.WriteLine("before failure");
            throw canceled ? new OperationCanceledException() : new InvalidOperationException("test failure");
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
