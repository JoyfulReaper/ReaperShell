using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class ShellHistoryPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ReaperShell.ShellHistoryPersistenceTests",
        Guid.NewGuid().ToString("N"));

    public ShellHistoryPersistenceTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for files that may still be held by test infrastructure.
        }
    }

    [Fact]
    public void HistorySurvivesCreatingASecondSession()
    {
        var historyPath = GetHistoryPath();
        var firstSession = new ShellSessionState(historyPath);

        firstSession.RecordHistory("echo one");
        firstSession.RecordHistory("echo two");

        var secondSession = new ShellSessionState(historyPath);

        Assert.Equal(["echo one", "echo two"], secondSession.GetHistory());
    }

    [Fact]
    public void AdjacentDuplicateSuppressionPersistsAcrossSessions()
    {
        var historyPath = GetHistoryPath();
        new ShellSessionState(historyPath).RecordHistory("echo same");

        var secondSession = new ShellSessionState(historyPath);
        secondSession.RecordHistory("echo same");

        Assert.Equal(["echo same"], secondSession.GetHistory());
        Assert.Equal(["echo same"], File.ReadAllLines(historyPath));
    }

    [Fact]
    public void BlankCommandsAreIgnored()
    {
        var historyPath = GetHistoryPath();
        var session = new ShellSessionState(historyPath);

        session.RecordHistory(string.Empty);
        session.RecordHistory(" \t ");

        Assert.Empty(session.GetHistory());
        Assert.False(File.Exists(historyPath));
    }

    [Fact]
    public async Task LeadingSpaceCommandExecutesWithoutBeingPersisted()
    {
        var historyPath = GetHistoryPath();
        var session = new ShellSessionState(historyPath);
        var (host, context) = CreateHost(session);

        var result = await RunCommandAsync(host, context, " echo secret-value");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("secret-value", result.StdOut);
        Assert.Empty(session.GetHistory());
        Assert.False(File.Exists(historyPath));
    }

    [Fact]
    public async Task HistoryClearClearsPersistentHistory()
    {
        var historyPath = GetHistoryPath();
        var session = new ShellSessionState(historyPath);
        session.RecordHistory("echo one");
        var (host, context) = CreateHost(session);

        var result = await RunCommandAsync(host, context, "history clear");
        var restartedSession = new ShellSessionState(historyPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("History cleared.", result.StdOut);
        Assert.Empty(session.GetHistory());
        Assert.Empty(restartedSession.GetHistory());
        Assert.Empty(File.ReadAllLines(historyPath));
    }

    [Fact]
    public void HistoryCapKeepsNewestTenThousandEntriesOnDiskAndInMemory()
    {
        var historyPath = GetHistoryPath();
        File.WriteAllLines(historyPath, Enumerable.Range(0, 10_000).Select(index => $"echo {index}"));

        var session = new ShellSessionState(historyPath);
        session.RecordHistory("echo 10000");
        var history = session.GetHistory();
        var persistedHistory = File.ReadAllLines(historyPath);

        Assert.Equal(10_000, history.Count);
        Assert.Equal("echo 1", history[0]);
        Assert.Equal("echo 10000", history[^1]);
        Assert.Equal(history, persistedHistory);
    }

    [Fact]
    public async Task HistoryCommandIsNotRecordedAndStillPrintsPersistedEntries()
    {
        var historyPath = GetHistoryPath();
        new ShellSessionState(historyPath).RecordHistory("echo earlier");
        var session = new ShellSessionState(historyPath);
        var (host, context) = CreateHost(session);

        var result = await RunCommandAsync(host, context, "history");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1: echo earlier", result.StdOut);
        Assert.Equal(["echo earlier"], session.GetHistory());
        Assert.Equal(["echo earlier"], File.ReadAllLines(historyPath));
    }

    private string GetHistoryPath()
    {
        return Path.Combine(_root, "history");
    }

    private (ShellHost Host, ShellContext Context) CreateHost(ShellSessionState sessionState)
    {
        var parser = new CommandParser();
        var registry = new CommandRegistry();
        var processRunner = new ProcessRunner(sessionState);
        var host = new ShellHost(
            parser,
            registry,
            new ShellLifetime(),
            processRunner,
            new ShellSettings(),
            _root,
            sessionState);
        registry.RegisterBuiltIn(new EchoCommand());
        registry.RegisterBuiltIn(new HistoryCommand(sessionState));
        return (host, CreateContext());
    }

    private ShellContext CreateContext()
    {
        return new ShellContext(
            new StringWriter(),
            new StringWriter(),
            new DirectoryInfo(_root),
            services: null,
            CancellationToken.None);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCommandAsync(
        ShellHost host,
        ShellContext context,
        string commandText)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var runContext = new ShellContext(
            stdout,
            stderr,
            context.WorkingDirectory,
            services: null,
            CancellationToken.None);
        var exitCode = await host.RunCommandAsync(runContext, commandText, CancellationToken.None);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

}
