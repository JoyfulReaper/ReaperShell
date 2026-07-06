using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

internal sealed class CommandListService
{
    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CommandRepoContextLoader loader,
        CancellationToken cancellationToken)
    {
        return ExecuteCoreAsync(context, args, loader, cancellationToken);
    }

    private static async Task<int> ExecuteCoreAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CommandRepoContextLoader loader,
        CancellationToken cancellationToken)
    {
        if (args.Count != 2)
        {
            context.WriteErrorLine("Usage: command list <repo>");
            return 1;
        }

        var repoContext = await loader.LoadAsync(context, args[1], cancellationToken);
        if (repoContext is null)
        {
            return 1;
        }

        var commandProjects = CommandProjectDiscovery.DiscoverProjects(repoContext.CommandsRoot);
        if (commandProjects.Count == 0)
        {
            context.WriteLine("No command projects were found.");
            return 0;
        }

        foreach (var commandProject in commandProjects)
        {
            context.WriteLine($"{Path.GetFileNameWithoutExtension(commandProject)} | {commandProject}");
        }

        return 0;
    }
}
