using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

internal sealed class RepoPublishService
{
    private readonly ProcessRunner _processRunner;
    private readonly RepoRegistryService _registry;

    public RepoPublishService(RepoRegistryService registry, ProcessRunner processRunner)
    {
        _registry = registry;
        _processRunner = processRunner;
    }

    public async Task<int> PublishAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryParsePublishArgs(context, args, out var repoName, out var ownerRepo, out var visibility))
        {
            return 1;
        }

        if (!_registry.TryGetRepoByName(repoName, context, out var repo))
        {
            return 1;
        }

        if (repo.IsGitRepo)
        {
            context.WriteErrorLine(
                $"Repo '{repo.Name}' is already Git-backed. Use 'repo push' or 'repo save' instead.");
            return 1;
        }

        if (!Directory.Exists(repo.LocalPath))
        {
            context.WriteErrorLine($"Repo path does not exist: {repo.LocalPath}");
            return 1;
        }

        var manifestPath = Path.Combine(repo.LocalPath, "shellpack.json");
        if (!File.Exists(manifestPath))
        {
            context.WriteErrorLine($"shellpack.json was not found: {manifestPath}");
            return 1;
        }

        if (!ExternalCommandResolver.TryResolveExecutable("gh", out var ghExecutable))
        {
            context.WriteErrorLine("GitHub CLI 'gh' was not found. Install and authenticate it first.");
            return 1;
        }

        var ghArguments = new List<string>
        {
            "repo",
            "create",
            ownerRepo,
            "--source",
            repo.LocalPath,
            "--remote",
            "origin",
            "--push",
            visibility
        };

        var ghResult = await _processRunner.RunAsync(
            ghExecutable,
            ghArguments,
            repo.LocalPath,
            context.WriteLine,
            context.WriteErrorLine,
            cancellationToken: cancellationToken);

        if (ghResult.ExitCode != 0)
        {
            return ghResult.ExitCode;
        }

        repo.IsGitRepo = true;
        repo.Source = ownerRepo;
        await _registry.SaveSettingsAsync(cancellationToken);

        context.WriteLine($"Published repo '{repo.Name}' to {ownerRepo}.");
        context.WriteLine($"repo status {repo.Name}");
        context.WriteLine($"repo save {repo.Name} \"message\"");
        return 0;
    }

    private static bool TryParsePublishArgs(
        ShellContext context,
        IReadOnlyList<string> args,
        out string repoName,
        out string ownerRepo,
        out string visibility)
    {
        repoName = string.Empty;
        ownerRepo = string.Empty;
        visibility = "--private";

        if (args.Count < 3)
        {
            context.WriteErrorLine("Usage: repo publish <name> <owner/repo> [--private|--public]");
            return false;
        }

        repoName = args[1];
        ownerRepo = args[2];

        var sawPrivate = false;
        var sawPublic = false;
        for (var index = 3; index < args.Count; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--private", StringComparison.OrdinalIgnoreCase))
            {
                sawPrivate = true;
                continue;
            }

            if (string.Equals(arg, "--public", StringComparison.OrdinalIgnoreCase))
            {
                sawPublic = true;
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal) && arg is not "-")
            {
                context.WriteErrorLine($"Unknown option: {arg}");
                return false;
            }

            context.WriteErrorLine($"Unexpected argument: {arg}");
            return false;
        }

        if (sawPrivate && sawPublic)
        {
            context.WriteErrorLine("Choose only one of --private or --public.");
            return false;
        }

        visibility = sawPublic ? "--public" : "--private";
        return true;
    }
}
