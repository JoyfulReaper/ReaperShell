using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

internal sealed class CommandRemoveService
{
    private readonly CommandRepoContextLoader _loader;
    private readonly CommandPackManager _commandPackManager;

    public CommandRemoveService(CommandRepoContextLoader loader, CommandPackManager commandPackManager)
    {
        _loader = loader;
        _commandPackManager = commandPackManager;
    }

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 3)
        {
            context.WriteErrorLine(CommandCommandUsage.Remove);
            return 1;
        }

        var repoName = args[1];
        var commandName = args[2];
        if (!ShellNameValidator.IsLowerKebabCaseName(commandName))
        {
            context.WriteErrorLine("Command names must start with a lowercase letter and use lowercase kebab-case.");
            return 1;
        }

        var repoContext = await _loader.LoadAsync(context, repoName, cancellationToken);
        if (repoContext is null)
        {
            return 1;
        }

        string? commandDirectory;
        try
        {
            commandDirectory = CommandPackPathResolver.EnsurePathWithinRoot(
                repoContext.CommandsRoot,
                Path.Combine(repoContext.CommandsRoot, commandName),
                "Command directory");
        }
        catch (Exception ex)
        {
            context.WriteErrorLine(ex.Message);
            return 1;
        }

        if (!Directory.Exists(commandDirectory))
        {
            context.WriteErrorLine($"Command directory does not exist: {commandDirectory}");
            return 1;
        }

        try
        {
            Directory.Delete(commandDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            context.WriteErrorLine($"Failed to remove command directory: {ex.Message}");
            return 1;
        }

        context.WriteLine($"Removed command '{commandName}' from repo '{repoContext.Repo.Name}'.");
        if (_commandPackManager.IsLoaded(repoContext.Repo.Name))
        {
            context.WriteLine(
                $"Repo '{repoContext.Repo.Name}' is currently loaded. Run 'repo reload {repoContext.Repo.Name}' to update loaded commands.");
        }

        return 0;
    }
}
