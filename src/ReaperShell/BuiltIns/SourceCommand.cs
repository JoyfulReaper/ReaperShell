using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class SourceCommand : IShellCommand
{
    private readonly CommandRegistry _commandRegistry;
    private readonly EditorLauncher _editorLauncher;
    private readonly ShellSettings _settings;
    private readonly string _workspaceRoot;

    public SourceCommand(
        ShellSettings settings,
        CommandRegistry commandRegistry,
        EditorLauncher editorLauncher,
        string workspaceRoot)
    {
        _settings = settings;
        _commandRegistry = commandRegistry;
        _editorLauncher = editorLauncher;
        _workspaceRoot = workspaceRoot;
    }

    public string Name => "source";

    public string Description => "Shows or opens the source location for a command.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: source <command>");
            return 1;
        }

        var commandName = args[0];
        if (_settings.Aliases.TryGetValue(commandName, out var replacement))
        {
            context.WriteLine($"Alias expansion: {replacement}");
            return 0;
        }

        if (!_commandRegistry.TryGetDescriptor(commandName, out var descriptor))
        {
            context.WriteErrorLine($"Command '{commandName}' is not registered.");
            return 1;
        }

        if (descriptor.OriginKind == CommandOriginKind.BuiltIn)
        {
            context.WriteLine($"Source for '{commandName}' is part of the host project at {_workspaceRoot}.");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(descriptor.PackPath))
        {
            context.WriteErrorLine($"No command pack path is recorded for '{commandName}'.");
            return 1;
        }

        context.WriteLine($"Opening pack source: {descriptor.PackPath}");
        return await _editorLauncher.TryOpenAsync(context, descriptor.PackPath, cancellationToken) ? 0 : 1;
    }
}
