using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class CatCommand : IShellCommand
{
    public string Name => "cat";

    public string Description => "Prints a text file.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: cat <file>");
            return 1;
        }

        var filePath = Path.GetFullPath(args[0], context.WorkingDirectory.FullName);
        if (!File.Exists(filePath))
        {
            context.WriteErrorLine($"File does not exist: {filePath}");
            return 1;
        }

        try
        {
            var contents = await File.ReadAllTextAsync(filePath, cancellationToken);
            context.WriteLine(contents);
            return 0;
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Failed to read file: {ex.Message}");
            return 1;
        }
    }
}
