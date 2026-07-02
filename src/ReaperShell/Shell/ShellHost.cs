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

    public async Task RunAsync(ShellContext context, CancellationToken cancellationToken)
    {
        context.WriteLine("REAPER SHELL v0.1");
        context.WriteLine("Live command environment online.");

        while (!_lifetime.ExitRequested && !cancellationToken.IsCancellationRequested)
        {
            Console.Write("rsh> ");
            var input = Console.ReadLine();
            if (input is null)
            {
                break;
            }

            var tokens = _commandParser.Parse(input);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (!_commandRegistry.TryGet(tokens[0], out var command))
            {
                context.WriteErrorLine($"Unknown command: {tokens[0]}");
                continue;
            }

            try
            {
                await command.ExecuteAsync(context, tokens.Skip(1).ToArray(), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                context.WriteErrorLine("Command canceled.");
            }
            catch (Exception ex)
            {
                context.WriteErrorLine($"Command failed: {ex.Message}");
            }
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
