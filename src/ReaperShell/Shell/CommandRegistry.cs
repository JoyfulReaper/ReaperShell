using ReaperShell.Abstractions;

namespace ReaperShell.Shell;

public sealed class CommandRegistry
{
    private readonly Dictionary<string, IShellCommand> _builtIns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IShellCommand> _pluginCommands = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterBuiltIn(IShellCommand command)
    {
        if (!_builtIns.TryAdd(command.Name, command))
        {
            throw new InvalidOperationException($"Built-in command '{command.Name}' is already registered.");
        }
    }

    public bool RegisterPlugin(IShellCommand command)
    {
        if (_builtIns.ContainsKey(command.Name) || _pluginCommands.ContainsKey(command.Name))
        {
            return false;
        }

        _pluginCommands.Add(command.Name, command);
        return true;
    }

    public bool Unregister(string commandName)
    {
        return _pluginCommands.Remove(commandName);
    }

    public bool TryGet(string commandName, out IShellCommand command)
    {
        if (_builtIns.TryGetValue(commandName, out command!))
        {
            return true;
        }

        return _pluginCommands.TryGetValue(commandName, out command!);
    }

    public IReadOnlyList<IShellCommand> GetAllCommands()
    {
        return _builtIns.Values
            .Concat(_pluginCommands.Values)
            .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
