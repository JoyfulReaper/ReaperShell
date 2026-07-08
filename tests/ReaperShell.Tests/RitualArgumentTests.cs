using Xunit;

namespace ReaperShell.Tests;

public sealed class RitualArgumentTests
{
    [Theory]
    [InlineData("ritual run greet --continue-on-error Kyle Debug")]
    [InlineData("ritual run greet Kyle Debug --continue-on-error")]
    public async Task ContinueOnErrorCanAppearAroundRitualArguments(string commandText)
    {
        using var harness = ScriptTestHarness.Create();
        var ritualPath = Path.Combine(harness.StateDirectory, "rituals", "greet.rsh");
        await File.WriteAllTextAsync(
            ritualPath,
            """
echo hello $1
echo config $2
echo all args: $*
""");

        var result = await harness.RunAutomationAsync(commandText);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello Kyle", result.StdOut);
        Assert.Contains("config Debug", result.StdOut);
        Assert.Contains("all args: Kyle Debug", result.StdOut);
        Assert.DoesNotContain("--continue-on-error", result.StdOut);
    }
}
