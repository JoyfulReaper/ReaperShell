using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

internal sealed class CommandScaffoldService
{
    private readonly CommandRepoContextLoader _loader;
    private readonly CommandScaffoldOptionsParser _optionsParser;
    private readonly CommandTemplateRenderer _templateRenderer;

    public CommandScaffoldService(
        CommandRepoContextLoader loader,
        CommandScaffoldOptionsParser optionsParser,
        CommandTemplateRenderer templateRenderer)
    {
        _loader = loader;
        _optionsParser = optionsParser;
        _templateRenderer = templateRenderer;
    }

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (!_optionsParser.TryParseNewCommandArgs(context, args, out var options))
        {
            return 1;
        }

        var repoContext = await _loader.LoadAsync(context, options.RepoName, cancellationToken);
        if (repoContext is null)
        {
            return 1;
        }

        var commandDirectory = Path.Combine(repoContext.CommandsRoot, options.CommandName);
        if (Directory.Exists(commandDirectory))
        {
            context.WriteErrorLine($"The command directory already exists: {commandDirectory}");
            return 1;
        }

        var className = _templateRenderer.ToPascalCase(options.CommandName) + "Command";
        var commandProjectPath = Path.Combine(commandDirectory, className + _templateRenderer.GetProjectFileExtension(options.Language));
        var commandSourcePath = Path.Combine(commandDirectory, className + _templateRenderer.GetSourceFileExtension(options.Language));

        Directory.CreateDirectory(commandDirectory);
        try
        {
            await File.WriteAllTextAsync(
                commandProjectPath,
                _templateRenderer.GetProjectFileContents(workspaceRoot, commandDirectory, className, options.Language),
                cancellationToken);

            await File.WriteAllTextAsync(
                commandSourcePath,
                _templateRenderer.GetTemplateSource(options.Template, options.Language, className, options.CommandName),
                cancellationToken);
        }
        catch
        {
            if (Directory.Exists(commandDirectory))
            {
                Directory.Delete(commandDirectory, recursive: true);
            }

            throw;
        }

        context.WriteLine($"Created command '{options.CommandName}' in repo '{repoContext.Repo.Name}'.");
        context.WriteLine(commandProjectPath);
        context.WriteLine(commandSourcePath);
        return 0;
    }
}
