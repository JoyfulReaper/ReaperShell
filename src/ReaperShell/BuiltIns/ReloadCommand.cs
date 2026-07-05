using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class ReloadCommand : IShellCommand
{
    private readonly ShellHost _host;

    public ReloadCommand(ShellHost host)
    {
        _host = host;
    }

    public string Name => "reload";

    public string Description => "Reloads settings and the active profile.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count != 0)
        {
            context.WriteErrorLine("Usage: reload");
            return 1;
        }

        return await _host.ReloadAsync(context, cancellationToken);
    }
}
