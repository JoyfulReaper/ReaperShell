using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class HeadCommand : IShellCommand
{
    public string Name => "head";

    public string Description => "Prints the first lines of a text file.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseArgs(context, args, out var count, out var filePath))
        {
            return 1;
        }

        if (!File.Exists(filePath))
        {
            context.WriteErrorLine($"File does not exist: {filePath}");
            return 1;
        }

        try
        {
            var lineCount = 0;
            await foreach (var line in File.ReadLinesAsync(filePath, cancellationToken))
            {
                context.WriteLine(line);
                lineCount++;
                if (lineCount >= count)
                {
                    break;
                }
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
        out string filePath)
    {
        count = 10;
        filePath = string.Empty;

        if (args.Count is 1)
        {
            filePath = Path.GetFullPath(args[0], context.WorkingDirectory.FullName);
            return true;
        }

        if (args.Count is 3 && string.Equals(args[0], "-n", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(args[1], out count) || count < 0)
            {
                context.WriteErrorLine("Usage: head [-n <count>] <file>");
                return false;
            }

            filePath = Path.GetFullPath(args[2], context.WorkingDirectory.FullName);
            return true;
        }

        context.WriteErrorLine("Usage: head [-n <count>] <file>");
        return false;
    }
}
