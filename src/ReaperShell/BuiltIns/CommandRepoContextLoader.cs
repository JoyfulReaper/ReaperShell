using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

internal sealed class CommandRepoContextLoader
{
    private readonly ShellSettings _settings;

    public CommandRepoContextLoader(ShellSettings settings)
    {
        _settings = settings;
    }

    public async Task<CommandRepoContext?> LoadAsync(
        ShellContext context,
        string repoName,
        CancellationToken cancellationToken)
    {
        if (!_settings.Repos.TryGetValue(repoName, out var foundRepo))
        {
            context.WriteErrorLine($"Repo '{repoName}' is not registered.");
            return null;
        }

        if (!Directory.Exists(foundRepo.LocalPath))
        {
            context.WriteErrorLine($"Repo path does not exist: {foundRepo.LocalPath}");
            return null;
        }

        var manifestPath = Path.Combine(foundRepo.LocalPath, "shellpack.json");
        if (!File.Exists(manifestPath))
        {
            context.WriteErrorLine($"shellpack.json was not found: {manifestPath}");
            return null;
        }

        CommandPackManifest manifest;
        try
        {
            manifest = await CommandPackManifest.LoadAsync(manifestPath, cancellationToken);
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Failed to load shellpack.json: {ex.Message}");
            return null;
        }

        try
        {
            var commandsRoot = CommandPackPathResolver.ResolveCommandsRoot(foundRepo.LocalPath, manifest.CommandsPath);
            return new CommandRepoContext(foundRepo, manifest, commandsRoot);
        }
        catch (Exception ex)
        {
            context.WriteErrorLine(ex.Message);
            return null;
        }
    }
}
