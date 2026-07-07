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
