using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class RmCommand : IShellCommand
{
    public string Name => "rm";

    public string Description => "Removes files and directories.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            context.WriteErrorLine("Usage: rm [-r|--recursive] [-f|--force] <path> [path...]");
            return Task.FromResult(1);
        }

        var recursive = false;
        var force = false;
        var targets = new List<string>();

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "-r":
                case "--recursive":
                    recursive = true;
                    break;
                case "-f":
                case "--force":
                    force = true;
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal) && arg is not "-")
                    {
                        context.WriteErrorLine($"Unknown option: {arg}");
                        return Task.FromResult(1);
                    }

                    targets.Add(arg);
                    break;
            }
        }

        if (targets.Count == 0)
        {
            context.WriteErrorLine("Usage: rm [-r|--recursive] [-f|--force] <path> [path...]");
            return Task.FromResult(1);
        }

        var hadError = false;
        foreach (var target in targets)
        {
            var targetPath = FileSystemCommandHelpers.ResolvePath(context, target);
            try
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                    continue;
                }

                if (Directory.Exists(targetPath))
                {
                    if (!recursive)
                    {
                        hadError = true;
                        context.WriteErrorLine($"Cannot remove directory without -r/--recursive: {targetPath}");
                        continue;
                    }

                    Directory.Delete(targetPath, recursive: true);
                    continue;
                }

                if (!force)
                {
                    hadError = true;
                    context.WriteErrorLine($"Path does not exist: {targetPath}");
                }
            }
            catch (Exception ex)
            {
                hadError = true;
                context.WriteErrorLine($"Failed to remove '{targetPath}': {ex.Message}");
            }
        }

        return Task.FromResult(hadError ? 1 : 0);
    }
}
