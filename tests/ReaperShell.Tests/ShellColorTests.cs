using ReaperShell.Abstractions;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class ShellColorTests
{
    [Theory]
    [InlineData(ShellColorMode.Auto, false, false, true, true)]
    [InlineData(ShellColorMode.Auto, false, true, true, false)]
    [InlineData(ShellColorMode.Auto, true, false, true, false)]
    [InlineData(ShellColorMode.Always, false, true, true, true)]
    [InlineData(ShellColorMode.Always, true, false, true, false)]
    [InlineData(ShellColorMode.Never, false, false, true, false)]
    [InlineData(ShellColorMode.Auto, false, false, false, false)]
    public void ColorPolicyRespectsModeRedirectionAndConsoleWriter(
        ShellColorMode mode,
        bool isRedirected,
        bool noColorEnvironmentSet,
        bool isConsoleWriter,
        bool expected)
    {
        var actual = ShellColorPolicy.ShouldUseColor(
            mode,
            isRedirected,
            noColorEnvironmentSet,
            isConsoleWriter);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SemanticWritesStayPlainWhenOutputIsRedirected()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(
            stdout,
            stderr,
            new DirectoryInfo(Path.GetTempPath()),
            services: null,
            CancellationToken.None,
            ShellColorMode.Always);

        context.WriteSuccessLine("success");
        context.WriteWarningLine("warning");
        context.WriteErrorLine("error");

        Assert.Equal(
            $"success{Environment.NewLine}warning{Environment.NewLine}",
            stdout.ToString());
        Assert.Equal($"error{Environment.NewLine}", stderr.ToString());
        Assert.DoesNotContain("\u001b[", stdout.ToString());
        Assert.DoesNotContain("\u001b[", stderr.ToString());
    }
}
