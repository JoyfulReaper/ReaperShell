using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class PromptFormattingTests
{
    [Fact]
    public void PathPromptEnabledUsesCurrentWorkingDirectory()
    {
        var leaf = Guid.NewGuid().ToString("N");
        var workingDirectory = Path.Combine(Path.GetTempPath(), "ReaperShell.PromptFormattingTests", leaf, "src");
        Directory.CreateDirectory(workingDirectory);

        var host = CreateHost(showPathInPrompt: true);
        var context = CreateContext(workingDirectory);

        var prompt = host.FormatPrompt(context);

        Assert.Contains(leaf, prompt);
        Assert.EndsWith("> ", prompt);
        Assert.DoesNotContain("rsh>", prompt);
    }

    [Fact]
    public void PathPromptDisabledFallsBackToRshPrompt()
    {
        var host = CreateHost(showPathInPrompt: false);
        var context = CreateContext(Path.Combine(Path.GetTempPath(), "ReaperShell.PromptFormattingTests", Guid.NewGuid().ToString("N")));

        var prompt = host.FormatPrompt(context);

        Assert.Equal("rsh> ", prompt);
    }

    [Fact]
    public void CursedPromptIncludesMarker()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0));
        curseState.Enable();

        var host = CreateHost(showPathInPrompt: false, curseState);
        var context = CreateContext(Path.Combine(Path.GetTempPath(), "ReaperShell.PromptFormattingTests", Guid.NewGuid().ToString("N")));

        var prompt = host.FormatPrompt(context);

        Assert.Equal("☠ rsh> ", prompt);
    }

    [Fact]
    public async Task PromptUpdatesAfterDirectoryChange()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReaperShell.PromptFormattingTests", Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "src", "ReaperShell");
        Directory.CreateDirectory(nested);

        var host = CreateHost(showPathInPrompt: true);
        var context = CreateContext(root);
        var cd = new CdCommand();

        var rootPrompt = host.FormatPrompt(context);
        Assert.Contains(Path.GetFileName(root), rootPrompt);
        Assert.EndsWith("> ", rootPrompt);

        var exitCode = await cd.ExecuteAsync(context, [ "src" ], CancellationToken.None);
        Assert.Equal(0, exitCode);
        var srcPrompt = host.FormatPrompt(context);
        Assert.Contains("src", srcPrompt);
        Assert.EndsWith("> ", srcPrompt);

        exitCode = await cd.ExecuteAsync(context, [ "ReaperShell" ], CancellationToken.None);
        Assert.Equal(0, exitCode);
        var nestedPrompt = host.FormatPrompt(context);
        Assert.Contains("ReaperShell", nestedPrompt);
        Assert.EndsWith("> ", nestedPrompt);
    }

    [Fact]
    public void HomeDirectoryCanBeAbbreviated()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrWhiteSpace(home));
        var prompt = ShellHost.FormatPrompt(Path.Combine(home, "GitHub", "ReaperShell"));

        Assert.StartsWith("~", prompt);
        Assert.EndsWith("> ", prompt);
    }

    private static ShellHost CreateHost(bool showPathInPrompt, ShellCurseState? curseState = null)
    {
        var settings = new ShellSettings { ShowPathInPrompt = showPathInPrompt };
        return new ShellHost(
            new CommandParser(),
            new CommandRegistry(),
            new ShellLifetime(),
            new ProcessRunner(),
            settings,
            Path.Combine(Path.GetTempPath(), "ReaperShell.PromptFormattingTests"),
            curseState: curseState);
    }

    private static ShellContext CreateContext(string workingDirectory)
    {
        return new ShellContext(
            TextWriter.Null,
            TextWriter.Null,
            new DirectoryInfo(workingDirectory),
            services: null,
            CancellationToken.None);
    }

    private sealed class SequenceCurseRandom : ICurseRandom
    {
        private readonly Queue<int> _values;

        public SequenceCurseRandom(params int[] values)
        {
            _values = new Queue<int>(values);
        }

        public int Next(int maxExclusive)
        {
            if (_values.Count == 0)
            {
                return 0;
            }

            return Math.Max(0, _values.Dequeue() % maxExclusive);
        }
    }
}
