using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class TouchCommand : IShellCommand
{
    public string Name => "touch";

    public string Description => "Creates files or updates their last write time.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            context.WriteErrorLine("Usage: touch <file> [file...]");
            return Task.FromResult(1);
        }

        var hadError = false;
        foreach (var arg in args)
        {
            var filePath = FileSystemCommandHelpers.ResolvePath(context, arg);
            try
            {
                if (Directory.Exists(filePath))
                {
                    throw new IOException("Path is a directory.");
                }

                if (File.Exists(filePath))
                {
                    File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
                }
                else
                {
                    var parentDirectory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrWhiteSpace(parentDirectory))
                    {
                        Directory.CreateDirectory(parentDirectory);
                    }

                    using var stream = File.Create(filePath);
                }
            }
            catch (Exception ex)
            {
                hadError = true;
                context.WriteErrorLine($"Failed to touch '{filePath}': {ex.Message}");
            }
        }

        return Task.FromResult(hadError ? 1 : 0);
    }
}
