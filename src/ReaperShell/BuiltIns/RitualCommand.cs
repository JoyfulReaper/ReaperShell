using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class RitualCommand : IShellCommand
{
    private readonly ShellHost _shellHost;
    private readonly string _stateDirectory;

    public RitualCommand(ShellHost shellHost, string stateDirectory)
    {
        _shellHost = shellHost;
        _stateDirectory = stateDirectory;
    }

    public string Name => "ritual";

    public string Description => "Runs and manages named ritual scripts.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            context.WriteErrorLine("Usage: ritual <list|run|path|new> ...");
            return 1;
        }

        return args[0].ToLowerInvariant() switch
        {
            "list" => ListRituals(context, args),
            "run" => await RunRitualAsync(context, args, cancellationToken),
            "path" => PrintRitualPath(context, args),
            "new" => await CreateRitualAsync(context, args, cancellationToken),
            _ => WriteUsage(context)
        };
    }

    private int ListRituals(ShellContext context, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: ritual list");
            return 1;
        }

        var ritualsDirectory = GetRitualsDirectory();
        Directory.CreateDirectory(ritualsDirectory);
        var ritualFiles = Directory.GetFiles(ritualsDirectory, "*.rsh", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ritualFiles.Length == 0)
        {
            context.WriteLine("No rituals are defined.");
            return 0;
        }

        foreach (var ritualFile in ritualFiles)
        {
            context.WriteLine(ritualFile!);
        }

        return 0;
    }

    private async Task<int> RunRitualAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count is not 2 and not 3)
        {
            context.WriteErrorLine("Usage: ritual run <name> [--continue-on-error]");
            return 1;
        }

        var continueOnError = false;
        if (args.Count == 3)
        {
            if (!string.Equals(args[2], "--continue-on-error", StringComparison.OrdinalIgnoreCase))
            {
                context.WriteErrorLine("Usage: ritual run <name> [--continue-on-error]");
                return 1;
            }

            continueOnError = true;
        }

        if (!TryGetRitualPath(args[1], context, out var ritualPath))
        {
            return 1;
        }

        return await _shellHost.RunRitualAsync(context, ritualPath, continueOnError, cancellationToken);
    }

    private int PrintRitualPath(ShellContext context, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: ritual path");
            return 1;
        }

        context.WriteLine(GetRitualsDirectory());
        return 0;
    }

    private async Task<int> CreateRitualAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 2)
        {
            context.WriteErrorLine("Usage: ritual new <name>");
            return 1;
        }

        if (!TryGetRitualPath(args[1], context, out var ritualPath))
        {
            return 1;
        }

        Directory.CreateDirectory(GetRitualsDirectory());
        if (File.Exists(ritualPath))
        {
            context.WriteErrorLine($"Ritual '{args[1]}' already exists.");
            return 1;
        }

        await File.WriteAllTextAsync(
            ritualPath,
            $$"""
# Ritual: {{args[1]}}
# One ReaperShell command per line.
repo list
plugins
""",
            cancellationToken);

        context.WriteLine($"Created ritual at {ritualPath}");
        return 0;
    }

    private string GetRitualsDirectory()
    {
        return Path.Combine(_stateDirectory, "rituals");
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

        ritualPath = Path.Combine(GetRitualsDirectory(), ritualName + ".rsh");
        return true;
    }

    private static int WriteUsage(ShellContext context)
    {
        context.WriteErrorLine("Usage: ritual <list|run|path|new> ...");
        return 1;
    }
}
