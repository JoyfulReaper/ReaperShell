using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class BannerCommand : IShellCommand
{
    public string Name => "banner";

    public string Description => "Prints the ReaperShell banner again.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        ShellBanner.Write(context);
        return Task.FromResult(0);
    }
}
