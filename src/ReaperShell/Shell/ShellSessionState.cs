namespace ReaperShell.Shell;

public sealed class ShellSessionState
{
    private const int MaximumHistoryCount = 10_000;

    private readonly object _gate = new();
    private readonly List<string> _history = [];
    private readonly Dictionary<string, string> _environmentVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _historyFilePath;

    public ShellSessionState()
    {
    }

    public ShellSessionState(string historyFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyFilePath);

        _historyFilePath = historyFilePath;
        LoadHistory();
    }

    public void RecordHistory(string commandText)
    {
        ArgumentNullException.ThrowIfNull(commandText);

        if (commandText.StartsWith(' '))
        {
            return;
        }

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

            if (_history.Count == MaximumHistoryCount)
            {
                var cappedHistory = _history.Skip(1).Append(normalizedCommand).ToArray();
                WriteHistory(cappedHistory);
                _history.RemoveAt(0);
                _history.Add(normalizedCommand);
                return;
            }

            AppendHistory(normalizedCommand);
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
            WriteHistory([]);
            _history.Clear();
        }
    }

    private void LoadHistory()
    {
        if (_historyFilePath is null || !File.Exists(_historyFilePath))
        {
            return;
        }

        var persistedLines = File.ReadAllLines(_historyFilePath);
        foreach (var line in persistedLines)
        {
            var normalizedCommand = line.Trim();
            if (string.IsNullOrWhiteSpace(normalizedCommand) ||
                (_history.Count > 0 && string.Equals(_history[^1], normalizedCommand, StringComparison.Ordinal)))
            {
                continue;
            }

            _history.Add(normalizedCommand);
        }

        if (_history.Count > MaximumHistoryCount)
        {
            _history.RemoveRange(0, _history.Count - MaximumHistoryCount);
        }

        if (!persistedLines.SequenceEqual(_history, StringComparer.Ordinal))
        {
            WriteHistory(_history);
        }
    }

    private void AppendHistory(string commandText)
    {
        if (_historyFilePath is null)
        {
            return;
        }

        EnsureHistoryDirectoryExists();
        File.AppendAllLines(_historyFilePath, [commandText]);
    }

    private void WriteHistory(IEnumerable<string> history)
    {
        if (_historyFilePath is null)
        {
            return;
        }

        EnsureHistoryDirectoryExists();
        File.WriteAllLines(_historyFilePath, history);
    }

    private void EnsureHistoryDirectoryExists()
    {
        var historyDirectory = Path.GetDirectoryName(_historyFilePath);
        if (!string.IsNullOrEmpty(historyDirectory))
        {
            Directory.CreateDirectory(historyDirectory);
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
