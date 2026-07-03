using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class HookCommand : IShellCommand
{
    private readonly ShellSettings _settings;
    private readonly string _stateDirectory;

    public HookCommand(ShellSettings settings, string stateDirectory)
    {
        _settings = settings;
        _stateDirectory = stateDirectory;
    }

    public string Name => "hook";

    public string Description => "Lists and manages shell event hooks.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            return WriteUsage(context);
        }

        return args[0].ToLowerInvariant() switch
        {
            "list" => ListHooks(context, args),
            "add" => await AddHookAsync(context, args, cancellationToken),
            "remove" => await RemoveHookAsync(context, args, cancellationToken),
            "clear" => await ClearHookAsync(context, args, cancellationToken),
            "events" => ListEvents(context, args),
            _ => WriteUsage(context)
        };
    }

    private int ListHooks(ShellContext context, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: hook list");
            return 1;
        }

        var configuredHooks = ShellHookEventNames.All
            .Select(eventName => new
            {
                EventName = eventName,
                Rituals = GetConfiguredRituals(eventName)
            })
            .Where(entry => entry.Rituals.Count > 0)
            .ToArray();

        if (configuredHooks.Length == 0)
        {
            context.WriteLine("No hooks are configured.");
            return 0;
        }

        foreach (var entry in configuredHooks)
        {
            context.WriteLine($"{entry.EventName} -> {string.Join(", ", entry.Rituals)}");
        }

        return 0;
    }

    private async Task<int> AddHookAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 3)
        {
            context.WriteErrorLine("Usage: hook add <event> <ritual-name>");
            return 1;
        }

        if (!TryValidateHookEvent(args[1], context, out var eventName) ||
            !TryGetRitualPath(args[2], context, out var ritualPath))
        {
            return 1;
        }

        if (!File.Exists(ritualPath))
        {
            context.WriteErrorLine($"Ritual '{args[2]}' does not exist.");
            return 1;
        }

        var rituals = GetOrCreateHookList(eventName);
        if (rituals.Contains(args[2], StringComparer.OrdinalIgnoreCase))
        {
            context.WriteErrorLine($"Hook '{eventName}' already includes ritual '{args[2]}'.");
            return 1;
        }

        rituals.Add(args[2]);
        await _settings.SaveAsync(_stateDirectory, cancellationToken);
        context.WriteLine($"Added ritual '{args[2]}' to hook '{eventName}'.");
        return 0;
    }

    private async Task<int> RemoveHookAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 3)
        {
            context.WriteErrorLine("Usage: hook remove <event> <ritual-name>");
            return 1;
        }

        if (!TryValidateHookEvent(args[1], context, out var eventName))
        {
            return 1;
        }

        if (!_settings.Hooks.TryGetValue(eventName, out var rituals) ||
            !RemoveRitual(rituals, args[2]))
        {
            context.WriteErrorLine($"Hook '{eventName}' does not include ritual '{args[2]}'.");
            return 1;
        }

        if (rituals.Count == 0)
        {
            _settings.Hooks.Remove(eventName);
        }

        await _settings.SaveAsync(_stateDirectory, cancellationToken);
        context.WriteLine($"Removed ritual '{args[2]}' from hook '{eventName}'.");
        return 0;
    }

    private async Task<int> ClearHookAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 2)
        {
            context.WriteErrorLine("Usage: hook clear <event>");
            return 1;
        }

        if (!TryValidateHookEvent(args[1], context, out var eventName))
        {
            return 1;
        }

        _settings.Hooks.Remove(eventName);
        await _settings.SaveAsync(_stateDirectory, cancellationToken);
        context.WriteLine($"Cleared hook '{eventName}'.");
        return 0;
    }

    private int ListEvents(ShellContext context, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: hook events");
            return 1;
        }

        foreach (var eventName in ShellHookEventNames.All)
        {
            context.WriteLine(eventName);
        }

        return 0;
    }

    private List<string> GetConfiguredRituals(string eventName)
    {
        if (!_settings.Hooks.TryGetValue(eventName, out var rituals) || rituals is null)
        {
            return [];
        }

        return rituals
            .Where(ritual => !string.IsNullOrWhiteSpace(ritual))
            .ToList();
    }

    private List<string> GetOrCreateHookList(string eventName)
    {
        if (!_settings.Hooks.TryGetValue(eventName, out var rituals) || rituals is null)
        {
            rituals = [];
            _settings.Hooks[eventName] = rituals;
        }

        return rituals;
    }

    private static bool RemoveRitual(List<string> rituals, string ritualName)
    {
        var existingRitual = rituals.FirstOrDefault(
            ritual => string.Equals(ritual, ritualName, StringComparison.OrdinalIgnoreCase));

        if (existingRitual is null)
        {
            return false;
        }

        rituals.Remove(existingRitual);
        return true;
    }

    private static bool TryValidateHookEvent(string eventName, ShellContext context, out string normalizedEventName)
    {
        normalizedEventName = eventName;
        if (ShellHookEventNames.IsSupported(eventName))
        {
            normalizedEventName = ShellHookEventNames.All.First(
                supportedEvent => string.Equals(supportedEvent, eventName, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        context.WriteErrorLine($"Unknown hook event: {eventName}");
        return false;
    }

    private bool TryGetRitualPath(string ritualName, ShellContext context, out string ritualPath)
    {
        ritualPath = string.Empty;
        if (string.IsNullOrWhiteSpace(ritualName) ||
            ritualName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            ritualName.Contains(Path.DirectorySeparatorChar) ||
            ritualName.Contains(Path.AltDirectorySeparatorChar) ||
            ritualName.Contains("..", StringComparison.Ordinal))
        {
            context.WriteErrorLine("Ritual names must be simple file names.");
            return false;
        }

        ritualPath = Path.Combine(_stateDirectory, "rituals", ritualName + ".rsh");
        return true;
    }

    private static int WriteUsage(ShellContext context)
    {
        context.WriteErrorLine("Usage: hook <list|add|remove|clear|events> ...");
        return 1;
    }
}
