using ReaperShell.Abstractions;

namespace ReaperShell.Shell;

public sealed class CommandRegistry
{
    private readonly Dictionary<string, CommandDescriptor> _builtIns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommandDescriptor> _pluginCommands = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterBuiltIn(IShellCommand command)
    {
        var descriptor = new CommandDescriptor(
            command.Name,
            command.Description,
            command,
            CommandOriginKind.BuiltIn,
            PackName: null,
            PackPath: null);

        if (!_builtIns.TryAdd(command.Name, descriptor))
        {
            throw new InvalidOperationException($"Built-in command '{command.Name}' is already registered.");
        }
    }

    public bool RegisterPlugin(IShellCommand command, string packName, string packPath)
    {
        if (_builtIns.ContainsKey(command.Name) || _pluginCommands.ContainsKey(command.Name))
        {
            return false;
        }

        _pluginCommands.Add(
            command.Name,
            new CommandDescriptor(
                command.Name,
                command.Description,
                command,
                CommandOriginKind.Plugin,
                packName,
                packPath));
        return true;
    }

    public bool Unregister(string commandName)
    {
        return _pluginCommands.Remove(commandName);
    }

    public bool TryGet(string commandName, out IShellCommand command)
    {
        if (_builtIns.TryGetValue(commandName, out var builtInDescriptor))
        {
            command = builtInDescriptor.Command;
            return true;
        }

        if (_pluginCommands.TryGetValue(commandName, out var pluginDescriptor))
        {
            command = pluginDescriptor.Command;
            return true;
        }

        command = null!;
        return false;
    }

    public bool TryGetDescriptor(string commandName, out CommandDescriptor descriptor)
    {
        if (_builtIns.TryGetValue(commandName, out var builtInDescriptor))
        {
            descriptor = builtInDescriptor;
            return true;
        }

        return _pluginCommands.TryGetValue(commandName, out descriptor!);
    }

    public bool IsBuiltIn(string commandName)
    {
        return _builtIns.ContainsKey(commandName);
    }

    public IReadOnlyList<CommandDescriptor> GetAllCommands()
    {
        return _builtIns.Values
            .Concat(_pluginCommands.Values)
            .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public int GetCommandCount()
    {
        return _builtIns.Count + _pluginCommands.Count;
    }
}

public sealed record CommandDescriptor(
    string Name,
    string Description,
    IShellCommand Command,
    CommandOriginKind OriginKind,
    string? PackName,
    string? PackPath);

public enum CommandOriginKind
{
    BuiltIn,
    Plugin
}
