using ReaperShell.Abstractions;

namespace ReaperShell.Shell;

public sealed class ShellHost
{
    private readonly CommandParser _commandParser;
    private readonly CommandRegistry _commandRegistry;
    private readonly ShellLifetime _lifetime;

    public ShellHost(CommandParser commandParser, CommandRegistry commandRegistry, ShellLifetime lifetime)
    {
        _commandParser = commandParser;
        _commandRegistry = commandRegistry;
        _lifetime = lifetime;
    }

    public async Task<int> RunInteractiveAsync(ShellContext context, CancellationToken cancellationToken)
    {
        WriteBanner(context);

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
        if (!File.Exists(scriptPath))
        {
            context.WriteErrorLine($"Script file does not exist: {scriptPath}");
            return 1;
        }

        WriteBanner(context);

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
                echoCommand: true,
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

    public async Task<int> RunCommandAsync(
        ShellContext context,
        string commandText,
        CancellationToken cancellationToken)
    {
        WriteBanner(context);
        return await ExecuteCommandAsync(context, commandText, echoCommand: false, cancellationToken);
    }

    private static void WriteBanner(ShellContext context)
    {
        context.WriteLine("REAPER SHELL v0.1");
        context.WriteLine("Live command environment online.");
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

        if (!_commandRegistry.TryGet(tokens[0], out var command))
        {
            context.WriteErrorLine($"Unknown command: {tokens[0]}");
            return 1;
        }

        try
        {
            return await command.ExecuteAsync(context, tokens.Skip(1).ToArray(), cancellationToken);
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
}

public sealed class ShellLifetime
{
    public bool ExitRequested { get; private set; }

    public void RequestExit()
    {
        ExitRequested = true;
    }
}
