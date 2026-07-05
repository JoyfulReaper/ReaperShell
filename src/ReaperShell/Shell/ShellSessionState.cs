namespace ReaperShell.Shell;

public sealed class ShellSessionState
{
    private readonly object _gate = new();
    private readonly List<string> _history = [];
    private readonly Dictionary<string, string> _environmentVariables = new(StringComparer.OrdinalIgnoreCase);

    public void RecordHistory(string commandText)
    {
        var normalizedCommand = commandText.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCommand))
        {
            return;
        }

        lock (_gate)
        {
            if (_history.Count > 0 &&
                string.Equals(_history[^1], normalizedCommand, StringComparison.Ordinal))
            {
                return;
            }

            _history.Add(normalizedCommand);
        }
    }

    public IReadOnlyList<string> GetHistory()
    {
        lock (_gate)
        {
            return _history.ToArray();
        }
    }

    public void ClearHistory()
    {
        lock (_gate)
        {
            _history.Clear();
        }
    }

    public IReadOnlyDictionary<string, string> GetEnvironmentVariables()
    {
        lock (_gate)
        {
            return new Dictionary<string, string>(_environmentVariables, StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool TryGetEnvironmentVariable(string name, out string value)
    {
        lock (_gate)
        {
            return _environmentVariables.TryGetValue(name, out value!);
        }
    }

    public void SetEnvironmentVariable(string name, string value)
    {
        lock (_gate)
        {
            _environmentVariables[name] = value;
        }
    }

    public bool RemoveEnvironmentVariable(string name)
    {
        lock (_gate)
        {
            return _environmentVariables.Remove(name);
        }
    }
}
