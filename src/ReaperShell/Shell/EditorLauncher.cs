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

        try
        {
            _processRunner.StartDetached(
                commandTokens[0],
                commandTokens.Skip(1).Append(targetPath).ToArray(),
                context.WorkingDirectory.FullName);
            return true;
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Failed to open editor: {ex.Message}");
            return false;
        }
    }

    private async Task<string?> ResolveEditorCommandAsync(
        ShellContext context,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_settings.EditorCommand))
        {
            return _settings.EditorCommand;
        }

        var editorFromEnvironment = Environment.GetEnvironmentVariable("RSH_EDITOR");
        if (!string.IsNullOrWhiteSpace(editorFromEnvironment))
        {
            return editorFromEnvironment;
        }

        editorFromEnvironment = Environment.GetEnvironmentVariable("EDITOR");
        if (!string.IsNullOrWhiteSpace(editorFromEnvironment))
        {
            return editorFromEnvironment;
        }

        return await IsCommandAvailableAsync("code", context, cancellationToken) ? "code" : null;
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
}
