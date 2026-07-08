using ReaperShell.Abstractions;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class CommandCompletionServiceTests
{
    private readonly CommandCompletionService _service = new();

    [Fact]
    public void CompletesCommandNames()
    {
        var result = TryComplete(
            "hist",
            [
                new CommandDescriptor("history", "", new StubCommand(), CommandOriginKind.BuiltIn, null, null),
                new CommandDescriptor("help", "", new StubCommand(), CommandOriginKind.BuiltIn, null, null),
                new CommandDescriptor("repo", "", new StubCommand(), CommandOriginKind.BuiltIn, null, null)
            ],
            []);

        Assert.Equal("history", result.UpdatedLine);
    }

    [Fact]
    public void CompletesAliases()
    {
        var result = TryComplete(
            "ll",
            [],
            ["ll"]);

        Assert.Equal("ll", result.UpdatedLine);
    }

    [Fact]
    public void CompletesBuiltInSubcommands()
    {
        var result = TryComplete(
            "repo sta",
            [
                new CommandDescriptor("repo", "", new StubCommand(), CommandOriginKind.BuiltIn, null, null)
            ],
            []);

        Assert.Equal("repo status", result.UpdatedLine);
    }

    [Fact]
    public void CompletesAdditionalBuiltInSubcommands()
    {
        Assert.Equal("curse enable", TryComplete("curse en", CurseCommand(), []).UpdatedLine);
        Assert.Equal("curse status", TryComplete("curse sta", CurseCommand(), []).UpdatedLine);
        Assert.Equal("curse set-failure-rate", TryComplete("curse set-f", CurseCommand(), []).UpdatedLine);
        Assert.Equal("pray hard", TryComplete("pray h", PrayCommand(), []).UpdatedLine);
        Assert.Equal("fortune status", TryComplete("fortune st", FortuneCommand(), []).UpdatedLine);
        Assert.Equal("env set", TryComplete("env se", EnvCommand(), []).UpdatedLine);
    }

    [Fact]
    public void CompletesBuiltInSubcommandsAfterTrailingWhitespace()
    {
        var result = TryComplete(
            "repo ",
            [
                new CommandDescriptor("repo", "", new StubCommand(), CommandOriginKind.BuiltIn, null, null)
            ],
            []);

        Assert.True(result.ShowCandidates);
        Assert.Contains("add", result.Candidates);
        Assert.Contains("status", result.Candidates);
    }

    [Fact]
    public void ShowsAdditionalSubcommandCandidatesAfterTrailingWhitespace()
    {
        var curse = TryComplete("curse ", CurseCommand(), []);
        Assert.True(curse.ShowCandidates);
        Assert.Contains("enable", curse.Candidates);
        Assert.Contains("disable", curse.Candidates);
        Assert.Contains("inspect", curse.Candidates);
        Assert.Contains("set-failure-rate", curse.Candidates);

        var pray = TryComplete("pray ", PrayCommand(), []);
        Assert.True(pray.ShowCandidates);
        Assert.Contains("status", pray.Candidates);
        Assert.Contains("hard", pray.Candidates);

        var fortune = TryComplete("fortune ", FortuneCommand(), []);
        Assert.True(fortune.ShowCandidates);
        Assert.Contains("read", fortune.Candidates);
        Assert.Contains("status", fortune.Candidates);
    }

    [Fact]
    public void ShowsCandidatesForMultipleCommands()
    {
        var result = TryComplete(
            "re",
            [
                new CommandDescriptor("repo", "", new StubCommand(), CommandOriginKind.BuiltIn, null, null),
                new CommandDescriptor("reload", "", new StubCommand(), CommandOriginKind.BuiltIn, null, null),
                new CommandDescriptor("remove", "", new StubCommand(), CommandOriginKind.BuiltIn, null, null)
            ],
            []);

        Assert.True(result.ShowCandidates);
        Assert.Contains("repo", result.Candidates);
        Assert.Contains("reload", result.Candidates);
        Assert.Contains("remove", result.Candidates);
    }

    [Fact]
    public void DoesNotCompleteBeyondTheFirstOrSecondToken()
    {
        var didComplete = _service.TryComplete(
            "repo add local-tools .\\sam",
            () => [new CommandDescriptor("repo", "", new StubCommand(), CommandOriginKind.BuiltIn, null, null)],
            () => [],
            out var result);

        Assert.False(didComplete);
        Assert.Null(result);
    }

    private CommandCompletionResult TryComplete(
        string input,
        IReadOnlyList<CommandDescriptor> commands,
        IReadOnlyList<string> aliases)
    {
        var didComplete = _service.TryComplete(
            input,
            () => commands,
            () => aliases,
            out var result);

        Assert.True(didComplete);
        Assert.NotNull(result);
        return result;
    }

    private static IReadOnlyList<CommandDescriptor> CurseCommand()
    {
        return [new CommandDescriptor("curse", "", new StubCommand(), CommandOriginKind.BuiltIn, null, null)];
    }

    private static IReadOnlyList<CommandDescriptor> PrayCommand()
    {
        return [new CommandDescriptor("pray", "", new StubCommand(), CommandOriginKind.BuiltIn, null, null)];
    }

    private static IReadOnlyList<CommandDescriptor> FortuneCommand()
    {
        return [new CommandDescriptor("fortune", "", new StubCommand(), CommandOriginKind.BuiltIn, null, null)];
    }

    private static IReadOnlyList<CommandDescriptor> EnvCommand()
    {
        return [new CommandDescriptor("env", "", new StubCommand(), CommandOriginKind.BuiltIn, null, null)];
    }

    private sealed class StubCommand : IShellCommand
    {
        public string Name => "stub";

        public string Description => string.Empty;

        public Task<int> ExecuteAsync(ShellContext context, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }
}
