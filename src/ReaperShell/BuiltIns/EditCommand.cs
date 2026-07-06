using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class EditCommand : IShellCommand
{
    private readonly EditorLauncher _editorLauncher;
    private readonly ShellSettings _settings;

    public EditCommand(ShellSettings settings, EditorLauncher editorLauncher)
    {
        _settings = settings;
        _editorLauncher = editorLauncher;
    }

    public string Name => "edit";

    public string Description => "Opens a file or directory in the configured editor.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (IsRepoOptionMode(args))
        {
            if (!TryParseRepoOptions(context, args, out var options))
            {
                return 1;
            }

            return await ExecuteRepoEditAsync(context, options!, cancellationToken);
        }

        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: edit <path>");
            return 1;
        }

        return await OpenPathAsync(context, args[0], cancellationToken);
    }

    private async Task<int> ExecuteRepoEditAsync(
        ShellContext context,
        RepoEditOptions options,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepo(options.RepoName, context, out var repo))
        {
            return 1;
        }

        if (!Directory.Exists(repo.LocalPath))
        {
            context.WriteErrorLine($"Path does not exist: {repo.LocalPath}");
            return 1;
        }

        if (options.CommandName is null)
        {
            return await OpenPathAsync(context, repo.LocalPath, cancellationToken);
        }

        if (!ShellNameValidator.IsLowerKebabCaseName(options.CommandName))
        {
            context.WriteErrorLine("Command names must start with a lowercase letter and use lowercase kebab-case.");
            return 1;
        }

        var manifestPath = Path.Combine(repo.LocalPath, "shellpack.json");
        if (!File.Exists(manifestPath))
        {
            context.WriteErrorLine($"shellpack.json was not found: {manifestPath}");
            return 1;
        }

        CommandPackManifest manifest;
        try
        {
            manifest = await CommandPackManifest.LoadAsync(manifestPath, cancellationToken);
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Failed to load shellpack.json: {ex.Message}");
            return 1;
        }

        string commandsRoot;
        try
        {
            commandsRoot = CommandPackPathResolver.ResolveCommandsRoot(repo.LocalPath, manifest.CommandsPath);
        }
        catch (Exception ex)
        {
            context.WriteErrorLine(ex.Message);
            return 1;
        }

        var commandDirectory = Path.Combine(commandsRoot, options.CommandName);
        if (!Directory.Exists(commandDirectory))
        {
            context.WriteErrorLine($"Command directory does not exist: {commandDirectory}");
            return 1;
        }

        return await OpenPathAsync(context, commandDirectory, cancellationToken);
    }

    private async Task<int> OpenPathAsync(
        ShellContext context,
        string path,
        CancellationToken cancellationToken)
    {
        var targetPath = Path.GetFullPath(path, context.WorkingDirectory.FullName);
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            context.WriteErrorLine($"Path does not exist: {targetPath}");
            return 1;
        }

        return await _editorLauncher.TryOpenAsync(context, targetPath, cancellationToken) ? 0 : 1;
    }

    private bool TryGetRepo(string repoName, ShellContext context, out CommandRepoSettings repo)
    {
        repo = null!;
        if (!_settings.Repos.TryGetValue(repoName, out var foundRepo))
        {
            context.WriteErrorLine($"Repo '{repoName}' is not registered.");
            return false;
        }

        repo = foundRepo;
        return true;
    }

    private static bool IsRepoOptionMode(IReadOnlyList<string> args)
    {
        return args.Count > 1 ||
               args.Any(arg =>
                   string.Equals(arg, "--repo", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(arg, "--command", StringComparison.OrdinalIgnoreCase) ||
                   arg.StartsWith("--", StringComparison.Ordinal));
    }

    private static bool TryParseRepoOptions(ShellContext context, IReadOnlyList<string> args, out RepoEditOptions? options)
    {
        options = default;
        string? repoName = null;
        string? commandName = null;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--repo", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadOptionValue(args, ref index, "--repo", out repoName, context))
                {
                    return false;
                }

                continue;
            }

            if (string.Equals(arg, "--command", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadOptionValue(args, ref index, "--command", out commandName, context))
                {
                    return false;
                }

                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                context.WriteErrorLine($"Unknown option: {arg}");
                context.WriteErrorLine(GetUsage());
                return false;
            }

            if (args.Count == 1)
            {
                context.WriteErrorLine(GetUsage());
                return false;
            }

            context.WriteErrorLine($"Unexpected argument: {arg}");
            context.WriteErrorLine(GetUsage());
            return false;
        }

        if (string.IsNullOrWhiteSpace(repoName))
        {
            context.WriteErrorLine("Missing value for --repo.");
            context.WriteErrorLine(GetUsage());
            return false;
        }

        options = new RepoEditOptions(repoName!, commandName);
        return true;
    }

    private static bool TryReadOptionValue(
        IReadOnlyList<string> args,
        ref int index,
        string optionName,
        out string? value,
        ShellContext context)
    {
        value = null;
        if (index + 1 >= args.Count || args[index + 1].StartsWith("-", StringComparison.Ordinal))
        {
            context.WriteErrorLine($"Missing value for {optionName}.");
            context.WriteErrorLine(GetUsage());
            return false;
        }

        value = args[++index];
        return true;
    }

    private static string GetUsage()
    {
        return "Usage: edit <path>\nUsage: edit --repo <repo> [--command <command-name>]";
    }

    private sealed record RepoEditOptions(string RepoName, string? CommandName);
}
