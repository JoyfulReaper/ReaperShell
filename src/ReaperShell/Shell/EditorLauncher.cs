using ReaperShell.Abstractions;

namespace ReaperShell.Shell;

public sealed class EditorLauncher
{
    private readonly CommandParser _commandParser = new();
    private readonly ProcessRunner _processRunner;
    private readonly ShellSettings _settings;

    public EditorLauncher(ShellSettings settings, ProcessRunner processRunner)
    {
        _settings = settings;
        _processRunner = processRunner;
    }

    public async Task<bool> TryOpenAsync(
        ShellContext context,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var editorCommand = await ResolveEditorCommandAsync(context, cancellationToken);
        if (editorCommand is null)
        {
            context.WriteErrorLine(
                "No editor is configured. Set RSH_EDITOR, EDITOR, or ShellSettings.EditorCommand, or install 'code'.");
            return false;
        }

        var commandTokens = _commandParser.Parse(editorCommand);
        if (commandTokens.Count == 0)
        {
            context.WriteErrorLine("The configured editor command is empty.");
            return false;
        }

        var launchTokens = await ResolveLaunchTokensAsync(commandTokens, context, cancellationToken);

        try
        {
            _processRunner.StartDetached(
                launchTokens[0],
                launchTokens.Skip(1).Append(targetPath).ToArray(),
                context.WorkingDirectory.FullName);
            return true;
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Failed to open editor: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> ResolveEditorCommandAsync(
        ShellContext context,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_settings.EditorCommand))
        {
            return await NormalizeEditorCommandAsync(_settings.EditorCommand, context, cancellationToken);
        }

        var editorFromEnvironment = Environment.GetEnvironmentVariable("RSH_EDITOR");
        if (!string.IsNullOrWhiteSpace(editorFromEnvironment))
        {
            return await NormalizeEditorCommandAsync(editorFromEnvironment, context, cancellationToken);
        }

        editorFromEnvironment = Environment.GetEnvironmentVariable("EDITOR");
        if (!string.IsNullOrWhiteSpace(editorFromEnvironment))
        {
            return await NormalizeEditorCommandAsync(editorFromEnvironment, context, cancellationToken);
        }

        if (await IsCommandAvailableAsync("code", context, cancellationToken))
        {
            return "code";
        }

        var visualStudioCodePath = GetWindowsVisualStudioCodePath();
        return visualStudioCodePath is null ? null : QuoteIfNeeded(visualStudioCodePath);
    }

    private async Task<bool> IsCommandAvailableAsync(
        string commandName,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        var probeExecutable = OperatingSystem.IsWindows() ? "where" : "which";
        var result = await _processRunner.RunAsync(
            probeExecutable,
            [commandName],
            context.WorkingDirectory.FullName,
            cancellationToken: cancellationToken);

        return result.ExitCode == 0;
    }

    private async Task<string> NormalizeEditorCommandAsync(
        string editorCommand,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        var commandTokens = _commandParser.Parse(editorCommand);
        if (commandTokens.Count == 0)
        {
            return editorCommand;
        }

        var resolvedExecutable = await ResolveExecutableTokenAsync(
            commandTokens[0],
            context,
            cancellationToken);

        if (string.Equals(resolvedExecutable, commandTokens[0], StringComparison.Ordinal))
        {
            return editorCommand;
        }

        return string.Join(
            " ",
            new[] { resolvedExecutable }
                .Concat(commandTokens.Skip(1))
                .Select(QuoteIfNeeded));
    }

    private async Task<IReadOnlyList<string>> ResolveLaunchTokensAsync(
        IReadOnlyList<string> commandTokens,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        var resolvedExecutable = await ResolveExecutableTokenAsync(
            commandTokens[0],
            context,
            cancellationToken);

        if (OperatingSystem.IsWindows() &&
            IsBatchScriptPath(resolvedExecutable))
        {
            return new[] { "cmd.exe", "/c", resolvedExecutable }
                .Concat(commandTokens.Skip(1))
                .ToArray();
        }

        return new[] { resolvedExecutable }
            .Concat(commandTokens.Skip(1))
            .ToArray();
    }

    private async Task<string> ResolveExecutableTokenAsync(
        string executableToken,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        var visualStudioCodePath = GetWindowsVisualStudioCodePath(executableToken);
        if (visualStudioCodePath is not null)
        {
            return visualStudioCodePath;
        }

        if (Path.IsPathRooted(executableToken))
        {
            return executableToken;
        }

        var commandPath = await ResolveCommandPathAsync(executableToken, context, cancellationToken);
        return commandPath ?? executableToken;
    }

    private async Task<string?> ResolveCommandPathAsync(
        string commandName,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        var probeExecutable = OperatingSystem.IsWindows() ? "where" : "which";
        var result = await _processRunner.RunAsync(
            probeExecutable,
            [commandName],
            context.WorkingDirectory.FullName,
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0)
        {
            return null;
        }

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }

    private static string QuoteIfNeeded(string commandPath)
    {
        return commandPath.Contains(' ') ? $"\"{commandPath}\"" : commandPath;
    }

    private static bool IsBatchScriptPath(string path)
    {
        return path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetWindowsVisualStudioCodePath(string? requestedCommand = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (requestedCommand is not null &&
            !IsVisualStudioCodeCommand(requestedCommand))
        {
            return null;
        }

        var candidatePaths = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Microsoft VS Code",
                "Code.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft VS Code",
                "Code.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft VS Code",
                "Code.exe")
        };

        return candidatePaths.FirstOrDefault(File.Exists);
    }

    private static bool IsVisualStudioCodeCommand(string commandName)
    {
        return string.Equals(commandName, "code", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(commandName, "code.cmd", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(commandName, "code-insiders", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(commandName, "code-insiders.cmd", StringComparison.OrdinalIgnoreCase);
    }
}
