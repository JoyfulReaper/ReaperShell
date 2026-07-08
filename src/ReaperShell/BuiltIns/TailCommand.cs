using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class TailCommand : IShellCommand
{
    public string Name => "tail";

    public string Description => "Prints the last lines of a text file.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseArgs(context, args, out var count, out var filePath, out var readFromInput))
        {
            return 1;
        }

        if (!readFromInput && !File.Exists(filePath))
        {
            context.WriteErrorLine($"File does not exist: {filePath}");
            return 1;
        }

        try
        {
            var lines = readFromInput
                ? await context.Input.ReadAllLinesAsync(cancellationToken)
                : await File.ReadAllLinesAsync(filePath, cancellationToken);
            var startIndex = Math.Max(0, lines.Length - count);
            foreach (var line in lines.Skip(startIndex))
            {
                context.WriteLine(line);
            }

            return 0;
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Failed to read file: {ex.Message}");
            return 1;
        }
    }

    private static bool TryParseArgs(
        ShellContext context,
        IReadOnlyList<string> args,
        out int count,
        out string filePath,
        out bool readFromInput)
    {
        count = 10;
        filePath = string.Empty;
        readFromInput = false;

        if (args.Count is 1)
        {
            filePath = Path.GetFullPath(args[0], context.WorkingDirectory.FullName);
            return true;
        }

        if (args.Count is 3 && string.Equals(args[0], "-n", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(args[1], out count) || count < 0)
            {
                context.WriteErrorLine("Usage: tail [-n <count>] <file>");
                return false;
            }

            filePath = Path.GetFullPath(args[2], context.WorkingDirectory.FullName);
            return true;
        }

        if (args.Count is 2 && string.Equals(args[0], "-n", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(args[1], out count) || count < 0)
            {
                context.WriteErrorLine("Usage: tail [-n <count>] <file>");
                return false;
            }

            readFromInput = true;
            return true;
        }

        if (args.Count is 0)
        {
            readFromInput = true;
            return true;
        }

        context.WriteErrorLine("Usage: tail [-n <count>] <file>");
        return false;
    }
}
