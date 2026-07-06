using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class CommandCommand : IShellCommand
{
    private readonly CommandListService _commandListService;
    private readonly CommandRepoContextLoader _commandRepoContextLoader;
    private readonly CommandRemoveService _commandRemoveService;
    private readonly CommandScaffoldService _commandScaffoldService;

    private readonly string _workspaceRoot;

    public CommandCommand(ShellSettings settings, CommandPackManager commandPackManager, string workspaceRoot)
    {
        _commandRepoContextLoader = new CommandRepoContextLoader(settings);
        _commandListService = new CommandListService();
        _commandScaffoldService = new CommandScaffoldService(
            _commandRepoContextLoader,
            new CommandScaffoldOptionsParser(),
            new CommandTemplateRenderer());
        _commandRemoveService = new CommandRemoveService(_commandRepoContextLoader, commandPackManager);
        _workspaceRoot = workspaceRoot;
    }

    public string Name => "command";

    public string Description => "Lists and forges commands inside an existing command pack.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            return Task.FromResult(WriteUsage(context));
        }

        return args[0].ToLowerInvariant() switch
        {
            "templates" => Task.FromResult(ListTemplates(context, args)),
            "list" => _commandListService.ExecuteAsync(context, args, _commandRepoContextLoader, cancellationToken),
            "new" => _commandScaffoldService.ExecuteAsync(context, args, _workspaceRoot, cancellationToken),
            "remove" => _commandRemoveService.ExecuteAsync(context, args, cancellationToken),
            "delete" => _commandRemoveService.ExecuteAsync(context, args, cancellationToken),
            "rm" => _commandRemoveService.ExecuteAsync(context, args, cancellationToken),
            _ => Task.FromResult(WriteUsage(context))
        };
    }

    private int ListTemplates(ShellContext context, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: command templates");
            return 1;
        }

        context.WriteLine("basic");
        context.WriteLine("file");
        context.WriteLine("process");
        return 0;
    }

    private static int WriteUsage(ShellContext context)
    {
        context.WriteErrorLine(CommandCommandUsage.TopLevel);
        return 1;
    }
}
