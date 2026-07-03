using ReaperShell.Abstractions;

namespace ReaperShell.Shell;

public sealed class ShellHost
{
    private const int AliasExpansionLimit = 8;

    private readonly CommandParser _commandParser;
    private readonly CommandRegistry _commandRegistry;
    private readonly ShellLifetime _lifetime;
    private readonly ShellSettings _settings;

    public ShellHost(
        CommandParser commandParser,
        CommandRegistry commandRegistry,
        ShellLifetime lifetime,
        ShellSettings settings)
    {
        _commandParser = commandParser;
        _commandRegistry = commandRegistry;
        _lifetime = lifetime;
        _settings = settings;
    }

    public async Task<int> RunInteractiveAsync(
        ShellContext context,
        string? profilePath,
        CancellationToken cancellationToken)
    {
        ShellBanner.Write(context);

        if (!string.IsNullOrWhiteSpace(profilePath))
        {
            await RunProfileAsync(context, profilePath, cancellationToken);
        }

        while (!_lifetime.ExitRequested && !cancellationToken.IsCancellationRequested)
        {
            Console.Write("rsh> ");
            var input = Console.ReadLine();
            if (input is null)
            {
                break;
            }

            await ExecuteCommandAsync(context, input, echoCommand: false, cancellationToken);
        }

        return 0;
    }

    public async Task<int> RunScriptAsync(
        ShellContext context,
        string scriptPath,
        bool continueOnError,
        CancellationToken cancellationToken)
    {
        ShellBanner.Write(context);
        return await ExecuteScriptFileAsync(
            context,
            scriptPath,
            continueOnError,
            echoCommand: true,
            cancellationToken);
    }

    public async Task<int> RunCommandAsync(
        ShellContext context,
        string commandText,
        CancellationToken cancellationToken)
    {
        ShellBanner.Write(context);
        return await ExecuteCommandAsync(context, commandText, echoCommand: false, cancellationToken);
    }

    public async Task<int> RunProfileAsync(
        ShellContext context,
        string profilePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(profilePath))
        {
            context.WriteErrorLine($"Profile file does not exist: {profilePath}");
            return 1;
        }

        context.WriteLine($"Running profile: {profilePath}");
        return await ExecuteScriptFileAsync(
            context,
            profilePath,
            continueOnError: true,
            echoCommand: true,
            cancellationToken);
    }

    public async Task<int> RunRitualAsync(
        ShellContext context,
        string ritualPath,
        bool continueOnError,
        CancellationToken cancellationToken)
    {
        return await ExecuteScriptFileAsync(
            context,
            ritualPath,
            continueOnError,
            echoCommand: true,
            cancellationToken);
    }

    private async Task<int> ExecuteCommandAsync(
        ShellContext context,
        string input,
        bool echoCommand,
        CancellationToken cancellationToken)
    {
        if (echoCommand)
        {
            context.WriteLine($"rsh> {input}");
        }

        var tokens = _commandParser.Parse(input);
        if (tokens.Count == 0)
        {
            return 0;
        }

        if (!TryResolveCommandTokens(tokens, echoCommand, context, out var resolvedTokens))
        {
            return 1;
        }

        if (!_commandRegistry.TryGet(resolvedTokens[0], out var command))
        {
            context.WriteErrorLine($"Unknown command: {resolvedTokens[0]}");
            return 1;
        }

        try
        {
            return await command.ExecuteAsync(context, resolvedTokens.Skip(1).ToArray(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            context.WriteErrorLine("Command canceled.");
            return 1;
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Command failed: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> ExecuteScriptFileAsync(
        ShellContext context,
        string scriptPath,
        bool continueOnError,
        bool echoCommand,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath))
        {
            context.WriteErrorLine($"Script file does not exist: {scriptPath}");
            return 1;
        }

        var exitCode = 0;
        foreach (var line in await File.ReadAllLinesAsync(scriptPath, cancellationToken))
        {
            var commandText = line.Trim();
            if (string.IsNullOrWhiteSpace(commandText) || commandText.StartsWith('#'))
            {
                continue;
            }

            var commandExitCode = await ExecuteCommandAsync(
                context,
                commandText,
                echoCommand,
                cancellationToken);

            if (commandExitCode != 0)
            {
                exitCode = commandExitCode;
                if (!continueOnError)
                {
                    return exitCode;
                }
            }

            if (_lifetime.ExitRequested || cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return exitCode;
    }

    private bool TryResolveCommandTokens(
        IReadOnlyList<string> tokens,
        bool echoExpandedCommand,
        ShellContext context,
        out IReadOnlyList<string> resolvedTokens)
    {
        resolvedTokens = tokens.ToArray();
        var expansions = 0;

        while (_settings.Aliases.TryGetValue(resolvedTokens[0], out var replacement))
        {
            expansions++;
            if (expansions > AliasExpansionLimit)
            {
                context.WriteErrorLine($"Alias expansion limit exceeded for '{tokens[0]}'.");
                return false;
            }

            var replacementTokens = _commandParser.Parse(replacement);
            if (replacementTokens.Count == 0)
            {
                context.WriteErrorLine($"Alias '{resolvedTokens[0]}' expands to an empty command.");
                return false;
            }

            resolvedTokens = replacementTokens
                .Concat(resolvedTokens.Skip(1))
                .ToArray();

            if (echoExpandedCommand)
            {
                context.WriteLine($"-> {string.Join(" ", resolvedTokens)}");
            }
        }

        return true;
    }
}

public sealed class ShellLifetime
{
    public bool ExitRequested { get; private set; }

    public void RequestExit()
    {
        ExitRequested = true;
    }
}
