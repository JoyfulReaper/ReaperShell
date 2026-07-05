using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class GrepCommand : IShellCommand
{
    public string Name => "grep";

    public string Description => "Searches a text file for matching lines.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseArgs(context, args, out var ignoreCase, out var pattern, out var filePath))
        {
            return 2;
        }

        if (!File.Exists(filePath))
        {
            context.WriteErrorLine($"File does not exist: {filePath}");
            return 2;
        }

        try
        {
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var matched = false;
            await foreach (var line in File.ReadLinesAsync(filePath, cancellationToken))
            {
                if (line.Contains(pattern, comparison))
                {
                    matched = true;
                    context.WriteLine(line);
                }
            }

            return matched ? 0 : 1;
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Failed to read file: {ex.Message}");
            return 2;
        }
    }

    private static bool TryParseArgs(
        ShellContext context,
        IReadOnlyList<string> args,
        out bool ignoreCase,
        out string pattern,
        out string filePath)
    {
        ignoreCase = false;
        pattern = string.Empty;
        filePath = string.Empty;

        if (args.Count == 2)
        {
            pattern = args[0];
            filePath = Path.GetFullPath(args[1], context.WorkingDirectory.FullName);
            return true;
        }

        if (args.Count == 3 && (string.Equals(args[0], "-i", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(args[0], "--ignore-case", StringComparison.OrdinalIgnoreCase)))
        {
            ignoreCase = true;
            pattern = args[1];
            filePath = Path.GetFullPath(args[2], context.WorkingDirectory.FullName);
            return true;
        }

        context.WriteErrorLine("Usage: grep [-i|--ignore-case] <pattern> <file>");
        return false;
    }
}
